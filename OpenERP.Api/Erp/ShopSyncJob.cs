using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpResult;

namespace OpenERP.Api.Erp;

/// <summary>同一时间只执行一轮全量同步；失败不覆盖上次成功数据。</summary>
internal sealed class ShopSyncJob(
    IServiceScopeFactory scopeFactory,
    IShopSyncNotifier notifier,
    TimeProvider clock,
    ILogger<ShopSyncJob> logger) : IShopSyncJob
{
    private const int StatusCheckConcurrency = 4;
    private const int ProgressInterval = 10;

    private readonly object _gate = new();
    private long _version;
    private ShopSyncStatus _state = new(false, null, null, 0, 0, 0, 0, 0, null, 0);

    public ShopSyncStatus Status
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public async Task<OpResult<ShopSyncStatus>> TryStartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_state.Running)
                return OpResults.Err("店铺与授权状态同步正在进行中，请稍后再试。");
        }

        long persistedVersion;
        using (var scope = scopeFactory.CreateScope())
        {
            var tokenStore = scope.ServiceProvider.GetRequiredService<IErpTokenStore>();
            var configuration = await tokenStore.GetConfigurationAsync(cancellationToken);
            if (configuration.IsErr)
                return configuration.Error;
            if (!configuration.Value.Configured)
                return OpResults.Err("尚未配置私有 ERP AccessToken。");

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var versionResult = await OpResults.TryInvokeAsync(() => db.ShopSyncStates
                .AsNoTracking()
                .Select(state => state.Version)
                .SingleOrDefaultAsync(cancellationToken));
            if (versionResult.IsErr)
                return OpResults.Err("读取店铺同步状态失败，请稍后重试。");

            persistedVersion = versionResult.Value;
        }

        lock (_gate)
        {
            if (_state.Running)
                return OpResults.Err("店铺与授权状态同步正在进行中，请稍后再试。");

            // 版本只在开始、完成与失败时递增；进度沿用开始版本，保证进程重启后版本仍单调。
            _version = Math.Max(_version, persistedVersion) + 1;
            _state = new ShopSyncStatus(
                true, clock.GetUtcNow(), null, 0, 0, 0, 0, 0, null, _version);
            var started = _state;
            _ = Task.Run(RunAsync);
            return OpResults.Ok(started);
        }
    }

    private async Task RunAsync()
    {
        var started = Status;
        var startedAt = started.StartedAt ?? clock.GetUtcNow();
        var startedVersion = started.Version;
        await SafePublishAsync(started);
        var previousCompletedAt = (DateTimeOffset?)null;
        var previousTotal = 0;
        var previousValid = 0;
        var previousInvalid = 0;
        var previousUnknown = 0;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var erp = scope.ServiceProvider.GetRequiredService<IErpClient>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenStore = scope.ServiceProvider.GetRequiredService<IErpTokenStore>();
            var cancellationToken = CancellationToken.None;

            // 并行状态查询不需要数据库；Token 在 fan-out 前只读一次。
            var tokenResult = await tokenStore.GetAsync(cancellationToken);
            if (tokenResult.IsErr)
            {
                await FinishFailureAsync(
                    tokenResult.Error.Message, startedAt, previousCompletedAt,
                    previousTotal, previousValid, previousInvalid, previousUnknown);
                return;
            }
            var accessToken = tokenResult.Value;

            var stateRow = await db.ShopSyncStates
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(cancellationToken);
            if (stateRow is not null)
            {
                previousCompletedAt = stateRow.LastCompletedAt;
                previousTotal = stateRow.Total;
                previousValid = stateRow.Valid;
                previousInvalid = stateRow.Invalid;
                previousUnknown = stateRow.Unknown;
            }
            else
            {
                stateRow = new ShopSyncState();
                db.ShopSyncStates.Add(stateRow);
            }

            stateRow.DeletedAt = null;
            stateRow.Version = startedVersion;
            stateRow.LastStartedAt = startedAt;
            stateRow.LastError = null;
            await db.SaveChangesAsync(cancellationToken);

            var shopsResult = await erp.GetAllShopsAsync(null, accessToken, cancellationToken);
            if (shopsResult.IsErr)
            {
                await FinishFailureAsync(
                    shopsResult.Error.Message, startedAt, previousCompletedAt,
                    previousTotal, previousValid, previousInvalid, previousUnknown);
                return;
            }

            var shops = shopsResult.Value;
            var counters = new SyncCounters();
            await PublishProgressAsync(shops.Count, counters);

            var (platformsResult, _) = await erp.GetPlatformsAsync(accessToken, cancellationToken);
            if (platformsResult.IsErr)
            {
                await FinishFailureAsync(
                    platformsResult.Error.Message, startedAt, previousCompletedAt,
                    previousTotal, previousValid, previousInvalid, previousUnknown);
                return;
            }

            var results = new ShopStatusResult[shops.Count];
            using var limit = new SemaphoreSlim(StatusCheckConcurrency);
            await Task.WhenAll(shops.Select(async (shop, index) =>
            {
                await limit.WaitAsync(cancellationToken);
                try
                {
                    var response = await erp.GetShopStatusAsync(shop.Sid, accessToken, cancellationToken);
                    var checkedAt = clock.GetUtcNow();
                    if (response.IsErr)
                    {
                        results[index] = new ShopStatusResult(shop, shop.Region, null, response.Error.Message, checkedAt);
                    }
                    else
                    {
                        results[index] = new ShopStatusResult(
                            shop, response.Value.Region ?? shop.Region, response.Value.IsTokenOk, null, checkedAt);
                    }

                    var checkedCount = counters.Record(results[index].IsTokenOk);
                    if (checkedCount % ProgressInterval == 0)
                        await PublishProgressAsync(shops.Count, counters);
                }
                finally
                {
                    limit.Release();
                }
            }));

            var completedAt = clock.GetUtcNow();
            var finalVersion = NextVersion();
            var persisted = await PersistAsync(
                db, stateRow, results, platformsResult.Value, counters, completedAt, finalVersion, cancellationToken);
            if (persisted.IsErr)
            {
                await FinishFailureAsync(
                    persisted.Error.Message, startedAt, previousCompletedAt,
                    previousTotal, previousValid, previousInvalid, previousUnknown);
                return;
            }

            var final = new ShopSyncStatus(
                false, startedAt, completedAt, results.Length, results.Length,
                counters.Valid, counters.Invalid, counters.Unknown, null, finalVersion);
            SetState(final);
            await SafePublishAsync(final);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "同步店铺与授权状态失败。");
            await FinishFailureAsync(
                "同步店铺与授权状态失败，请稍后重试。", startedAt, previousCompletedAt,
                previousTotal, previousValid, previousInvalid, previousUnknown);
        }
    }

    private async Task<OpResult.OpResult> PersistAsync(
        AppDbContext db,
        ShopSyncState stateRow,
        IReadOnlyList<ShopStatusResult> results,
        IReadOnlyList<ErpPlatform> platforms,
        SyncCounters counters,
        DateTimeOffset completedAt,
        long version,
        CancellationToken cancellationToken)
    {
        var existingShops = await db.Shops.IgnoreQueryFilters().ToDictionaryAsync(shop => shop.Sid, cancellationToken);
        var seenSids = new HashSet<int>();
        foreach (var result in results)
        {
            if (!int.TryParse(result.Shop.Sid, NumberStyles.None, CultureInfo.InvariantCulture, out var sid) || sid <= 0)
                return OpResults.Err("ERP 返回了无效的店铺 ID。");

            seenSids.Add(sid);
            if (!existingShops.TryGetValue(sid, out var shop))
            {
                shop = new Shop { Sid = sid };
                db.Shops.Add(shop);
            }

            shop.DeletedAt = null;
            shop.Name = Clip(result.Shop.Name, 200) ?? "";
            shop.Platform = Clip(result.Shop.Platform, 64) ?? "";
            shop.Region = Clip(result.Region, 64);
            shop.MemberId = Clip(result.Shop.MemberId, 128);
            shop.IsTokenOk = result.IsTokenOk;
            shop.TokenError = Clip(result.Error, 500);
            shop.SyncedAt = result.CheckedAt;
        }

        foreach (var stale in existingShops.Values.Where(shop => !seenSids.Contains(shop.Sid)))
            stale.DeletedAt ??= completedAt;

        var existingPlatforms = await db.ShopPlatforms.IgnoreQueryFilters().ToDictionaryAsync(platform => platform.Name, cancellationToken);
        var seenPlatforms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var platform in platforms)
        {
            var name = Clip(platform.Name, 64);
            if (string.IsNullOrEmpty(name))
                continue;

            seenPlatforms.Add(name);
            if (!existingPlatforms.TryGetValue(name, out var row))
            {
                row = new ShopPlatform { Name = name };
                db.ShopPlatforms.Add(row);
            }

            row.DeletedAt = null;
            row.DisplayName = Clip(platform.DisplayName, 100) ?? name;
            row.Icon = Clip(platform.Icon, 100);
            row.IsProduction = platform.IsProduction;
        }

        foreach (var stale in existingPlatforms.Values.Where(platform => !seenPlatforms.Contains(platform.Name)))
            stale.DeletedAt ??= completedAt;

        stateRow.Version = version;
        stateRow.LastCompletedAt = completedAt;
        stateRow.LastError = null;
        stateRow.Total = results.Count;
        stateRow.Valid = counters.Valid;
        stateRow.Invalid = counters.Invalid;
        stateRow.Unknown = counters.Unknown;
        await db.SaveChangesAsync(cancellationToken);
        return OpResults.Ok();
    }

    private async Task FinishFailureAsync(
        string message,
        DateTimeOffset startedAt,
        DateTimeOffset? previousCompletedAt,
        int total,
        int valid,
        int invalid,
        int unknown)
    {
        var error = Clip(message, 500) ?? "同步店铺与授权状态失败，请稍后重试。";
        var version = NextVersion();

        // 先落库再释放 Running，避免重试任务清除错误后旧任务又把过期错误写回。
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stateRow = await db.ShopSyncStates
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(CancellationToken.None);
            if (stateRow is null)
            {
                stateRow = new ShopSyncState();
                db.ShopSyncStates.Add(stateRow);
            }

            stateRow.DeletedAt = null;
            stateRow.Version = version;
            stateRow.LastError = error;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "记录店铺同步失败状态失败。");
        }

        var failed = new ShopSyncStatus(
            false, startedAt, previousCompletedAt, total, total, valid, invalid, unknown, error, version);
        SetState(failed);
        await SafePublishAsync(failed);
    }

    private async Task PublishProgressAsync(int total, SyncCounters counters)
    {
        ShopSyncStatus snapshot;
        lock (_gate)
        {
            // 进度不递增版本：同一次运行内客户端始终按“运行中”处理，完成后由新版本触发列表刷新。
            _state = _state with
            {
                Total = total,
                Checked = counters.Checked,
                Valid = counters.Valid,
                Invalid = counters.Invalid,
                Unknown = counters.Unknown
            };
            snapshot = _state;
        }

        await SafePublishAsync(snapshot);
    }

    /// <summary>为一次最终状态（完成或失败）分配新的修订号。</summary>
    private long NextVersion()
    {
        lock (_gate)
            return ++_version;
    }

    private void SetState(ShopSyncStatus state)
    {
        lock (_gate)
            _state = state;
    }

    private async Task SafePublishAsync(ShopSyncStatus status)
    {
        try
        {
            await notifier.PublishAsync(status, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "推送店铺同步状态失败。");
        }
    }

    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is null || trimmed.Length <= maxLength)
            return trimmed;

        return trimmed[..maxLength];
    }

    private sealed record ShopStatusResult(
        ErpShop Shop,
        string? Region,
        bool? IsTokenOk,
        string? Error,
        DateTimeOffset CheckedAt);

    private sealed class SyncCounters
    {
        private int _checked;
        private int _valid;
        private int _invalid;
        private int _unknown;

        public int Checked => Volatile.Read(ref _checked);

        public int Valid => Volatile.Read(ref _valid);

        public int Invalid => Volatile.Read(ref _invalid);

        public int Unknown => Volatile.Read(ref _unknown);

        public int Record(bool? isTokenOk)
        {
            if (isTokenOk == true)
                Interlocked.Increment(ref _valid);
            else if (isTokenOk == false)
                Interlocked.Increment(ref _invalid);
            else
                Interlocked.Increment(ref _unknown);

            return Interlocked.Increment(ref _checked);
        }
    }
}

/// <summary>通过 SignalR 广播同步状态；推送失败只记录日志。</summary>
internal sealed class ShopSyncNotifier(
    IHubContext<ErpHub> hub,
    ILogger<ShopSyncNotifier> logger) : IShopSyncNotifier
{
    public async Task PublishAsync(ShopSyncStatus status, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.All.SendAsync(ErpHub.ShopSyncUpdated, status, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "推送店铺同步状态失败。");
        }
    }
}

/// <summary>启动后先等待初始延迟，再按配置周期触发同步。</summary>
internal sealed class ShopSyncScheduler(
    IShopSyncJob job,
    IOptions<ShopSyncOptions> options,
    ILogger<ShopSyncScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var initialDelay = TimeSpan.FromSeconds(Math.Max(0, options.Value.InitialDelaySeconds));
            if (initialDelay > TimeSpan.Zero)
                await Task.Delay(initialDelay, stoppingToken);
            await TryStartAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.IntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TryStartAsync();
        }
        catch (OperationCanceledException)
        {
            // 应用停止。
        }
    }

    private async Task TryStartAsync()
    {
        var started = await job.TryStartAsync(CancellationToken.None);
        if (started.IsErr)
            logger.LogDebug("跳过店铺同步：{Message}", started.Error.Message);
    }
}
