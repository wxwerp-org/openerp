using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpResult;
using OpenERP.Api.Erp;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class ShopSyncJobTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string _databaseName = $"openerp_shop_sync_test_{Guid.NewGuid():N}";
    private string _adminConnectionString = "";
    private string _connectionString = "";
    private bool _databaseCreated;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _adminConnectionString = await fixture.GetPostgresConnectionStringAsync(cancellationToken);
        _connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""CREATE DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _databaseCreated = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_databaseCreated)
            return;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""DROP DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Full_sync_upserts_shops_replaces_platforms_and_marks_unknown()
    {
        // Given 本地有旧店铺与旧平台，上游返回新增、变更、失效三家店。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        await SeedAsync(ct);
        await using var db = CreateDbContext();
        var originalIds = await db.Shops.AsNoTracking().ToDictionaryAsync(shop => shop.Sid, shop => shop.Id, ct);
        var includePreviousShop = false;
        var erp = new TestErpClient
        {
            GetAllShops = (_, _, _) => Task.FromResult(OpResults.Ok<IReadOnlyList<ErpShop>>(includePreviousShop
                ? [new("99", "恢复店铺", "TemuPop", null, null), new("30", "同店再次更新", "TemuPop", "US", "member-30")]
                : [new("30", "店铺三十（新）", "TemuPop", "US", "member-30"),
                new("6", "AE 店", "AliExpress", "GB", "member-6"),
                new("9", "失效店", "TemuPop", null, null)])),
            GetPlatforms = (_, _) => Task.FromResult<(OpResult<IReadOnlyList<ErpPlatform>>, int)>((
                OpResults.Ok<IReadOnlyList<ErpPlatform>>([
                    new("TemuPop", "Temu半托管", "icon-temupop", true, 2),
                    new("AliExpress", "速卖通", "icon-aliexpress", true, 1)
                ]), 200)),
            GetShopStatus = (sid, _, _) => Task.FromResult(sid switch
            {
                "30" => OpResults.Ok(new ErpShopStatus(true, "US", true)),
                "6" => OpResults.Ok(new ErpShopStatus(false, "GB", false)),
                _ => OpResults.Err("状态读取失败")
            })
        };
        var (job, notifier, provider) = await CreateJobAsync(erp, ct);
        using var providerLifetime = provider;

        // When 触发一次全量同步。
        var started = await job.TryStartAsync(ct);
        if (started.IsErr)
            throw new XunitException(started.Error.Message);
        Assert.Equal(8, started.Value.Version);
        var final = await WaitForIdleAsync(job, notifier, ct);

        // Then 新增与变更店铺按 SID 写入，旧店铺删除，失败店铺记为未知。
        var shops = await db.Shops.AsNoTracking().OrderBy(shop => shop.Sid).ToArrayAsync(ct);
        Assert.Equal([6, 9, 30], shops.Select(shop => shop.Sid));
        var shop30 = shops.Single(shop => shop.Sid == 30);
        Assert.Equal("店铺三十（新）", shop30.Name);
        Assert.Equal("member-30", shop30.MemberId);
        Assert.True(shop30.IsTokenOk);
        Assert.NotNull(shop30.SyncedAt);
        Assert.Equal(originalIds[30], shop30.Id);
        Assert.All(shops, shop => Assert.Equal(7, shop.Id.Version));
        var shop6 = shops.Single(shop => shop.Sid == 6);
        Assert.Equal("GB", shop6.Region);
        Assert.False(shop6.IsTokenOk);
        var shop9 = shops.Single(shop => shop.Sid == 9);
        Assert.Null(shop9.IsTokenOk);
        Assert.Equal("状态读取失败", shop9.TokenError);
        Assert.DoesNotContain(shops, shop => shop.Sid == 99);

        var platforms = await db.ShopPlatforms.AsNoTracking().OrderBy(platform => platform.Name).ToArrayAsync(ct);
        Assert.Equal(["AliExpress", "TemuPop"], platforms.Select(platform => platform.Name));
        Assert.Equal("速卖通", platforms[0].DisplayName);
        var deleted = await db.Shops.IgnoreQueryFilters().AsNoTracking().SingleAsync(shop => shop.Sid == 99, ct);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(originalIds[99], deleted.Id);

        // Then 同步结果与计数落库，推送最终状态。
        var state = await db.ShopSyncStates.AsNoTracking().SingleAsync(ct);
        Assert.Equal(final.Version, state.Version);
        Assert.Equal(3, state.Total);
        Assert.Equal(1, state.Valid);
        Assert.Equal(1, state.Invalid);
        Assert.Equal(1, state.Unknown);
        Assert.NotNull(state.LastCompletedAt);
        Assert.Null(state.LastError);
        Assert.False(final.Running);
        Assert.Equal(9, final.Version);
        Assert.Equal(3, final.Total);
        Assert.Equal(3, final.Checked);
        Assert.Equal(1, final.Valid);
        Assert.Equal(1, final.Invalid);
        Assert.Equal(1, final.Unknown);
        Assert.Null(final.Error);
        Assert.Contains(notifier.Statuses, status => status.Running && status.Total == 3);
        Assert.Equal(final, notifier.Statuses[^1]);

        // When 同一店铺再次同步且此前缺失的店铺重新出现。Then 继续使用原 UUID。
        includePreviousShop = true;
        Assert.False((await job.TryStartAsync(ct)).IsErr);
        Assert.Null((await WaitForIdleAsync(job, notifier, ct)).Error);
        var current = await db.Shops.AsNoTracking().OrderBy(shop => shop.Sid).ToArrayAsync(ct);
        Assert.Equal([30, 99], current.Select(shop => shop.Sid));
        Assert.Equal("同店再次更新", current[0].Name);
        Assert.Equal("恢复店铺", current[1].Name);
        Assert.Equal(originalIds[30], current[0].Id);
        Assert.Equal(originalIds[99], current[1].Id);
    }

    [Fact]
    public async Task Upstream_failure_keeps_previous_shops_and_records_error()
    {
        // Given 本地已有上次成功数据，上游店铺列表读取失败。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var previousCompletedAt = ToDatabasePrecision(DateTimeOffset.UtcNow.AddMinutes(-30));
        await SeedAsync(ct, previousCompletedAt);
        var erp = new TestErpClient
        {
            GetAllShops = (_, _, _) => Task.FromResult<OpResult<IReadOnlyList<ErpShop>>>(OpResults.Err("ERP 请求超时，请重试。"))
        };
        var (job, notifier, provider) = await CreateJobAsync(erp, ct);
        using var providerLifetime = provider;

        // When 触发同步。
        Assert.False((await job.TryStartAsync(ct)).IsErr);
        var final = await WaitForIdleAsync(job, notifier, ct);

        // Then 店铺数据不变，完成时间保留上次成功时间，错误写入状态。
        Assert.False(final.Running);
        Assert.Equal(previousCompletedAt, final.CompletedAt);
        Assert.Equal(9, final.Version);
        Assert.Equal(2, final.Total);
        Assert.Equal(1, final.Valid);
        Assert.Equal(1, final.Invalid);
        Assert.Contains("ERP 请求超时", final.Error);

        await using var db = CreateDbContext();
        var shops = await db.Shops.AsNoTracking().OrderBy(shop => shop.Sid).ToArrayAsync(ct);
        Assert.Equal([30, 99], shops.Select(shop => shop.Sid));
        Assert.False(shops[0].IsTokenOk);
        var state = await db.ShopSyncStates.AsNoTracking().SingleAsync(ct);
        Assert.Equal(previousCompletedAt, state.LastCompletedAt);
        Assert.Contains("ERP 请求超时", state.LastError);
        Assert.Equal(final.Version, state.Version);
        Assert.Equal(1, state.Valid);
    }

    [Fact]
    public async Task Sync_run_uses_one_token_snapshot_for_shops_platforms_and_statuses()
    {
        // Given 本轮有两家店铺，授权状态请求可以并发执行。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var store = new TestErpTokenStore("test-token");
        var seenTokens = new ConcurrentBag<string>();
        var statusCalls = 0;
        var bothStatusesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStatuses = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var erp = new TestErpClient
        {
            GetAllShops = (_, token, _) =>
            {
                seenTokens.Add(token.Value);
                return Task.FromResult(OpResults.Ok<IReadOnlyList<ErpShop>>([
                    new("30", "店铺三十", "TemuPop", "US", "member-30"),
                    new("31", "店铺三十一", "TemuPop", "US", "member-31")
                ]));
            },
            GetPlatforms = (token, _) =>
            {
                seenTokens.Add(token.Value);
                return Task.FromResult<(OpResult<IReadOnlyList<ErpPlatform>>, int)>((
                    OpResults.Ok<IReadOnlyList<ErpPlatform>>([
                        new("TemuPop", "Temu半托管", "icon-temupop", true, 2)
                    ]), 200));
            },
            GetShopStatus = async (_, token, _) =>
            {
                seenTokens.Add(token.Value);
                if (Interlocked.Increment(ref statusCalls) == 2)
                    bothStatusesStarted.TrySetResult();
                await releaseStatuses.Task.WaitAsync(ct);
                return OpResults.Ok(new ErpShopStatus(true, "US", true));
            }
        };
        var (job, notifier, provider) = await CreateJobAsync(erp, ct, store);
        using var providerLifetime = provider;

        // When 执行一轮同步并等待两次状态请求同时进入。
        Assert.False((await job.TryStartAsync(ct)).IsErr);
        try
        {
            await bothStatusesStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally
        {
            releaseStatuses.TrySetResult();
        }
        var final = await WaitForIdleAsync(job, notifier, ct);

        // Then 同步成功，店铺、平台、状态查询使用本轮唯一一次读取的 Token。
        Assert.Null(final.Error);
        Assert.Equal(2, final.Valid);
        Assert.Equal(1, store.Reads);
        Assert.Equal(4, seenTokens.Count);
        Assert.All(seenTokens, token => Assert.Equal("test-token", token));
    }

    [Fact]
    public async Task Invalid_upstream_shop_id_fails_with_result_and_keeps_previous_data()
    {
        // Given 本地已有上次成功数据，上游返回无法解析的店铺 ID。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var previousCompletedAt = ToDatabasePrecision(DateTimeOffset.UtcNow.AddMinutes(-30));
        await SeedAsync(ct, previousCompletedAt);
        var erp = new TestErpClient
        {
            GetAllShops = (_, _, _) => Task.FromResult(OpResults.Ok<IReadOnlyList<ErpShop>>([
                new("not-a-number", "坏店铺", "TemuPop", "US", null)
            ]))
        };
        var (job, notifier, provider) = await CreateJobAsync(erp, ct);
        using var providerLifetime = provider;

        // When 触发同步。
        Assert.False((await job.TryStartAsync(ct)).IsErr);
        var final = await WaitForIdleAsync(job, notifier, ct);

        // Then 以业务错误结束，不抛给调度器，也不覆盖上次成功数据。
        Assert.False(final.Running);
        Assert.Contains("无效的店铺 ID", final.Error);
        Assert.Equal(previousCompletedAt, final.CompletedAt);

        await using var db = CreateDbContext();
        var shops = await db.Shops.AsNoTracking().OrderBy(shop => shop.Sid).ToArrayAsync(ct);
        Assert.Equal([30, 99], shops.Select(shop => shop.Sid));
        var state = await db.ShopSyncStates.AsNoTracking().SingleAsync(ct);
        Assert.Contains("无效的店铺 ID", state.LastError);
        Assert.Equal(previousCompletedAt, state.LastCompletedAt);
    }

    [Fact]
    public async Task Token_store_failure_returns_result_instead_of_throwing()
    {
        // Given Token 存储查询数据库时失败。
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSingleton<IErpClient>(new TestErpClient());
        services.AddSingleton<IErpTokenStore>(new FailingErpTokenStore());
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var job = new ShopSyncJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new RecordingNotifier(),
            TimeProvider.System,
            provider.GetRequiredService<ILogger<ShopSyncJob>>());

        // When 手动触发同步。
        var result = await job.TryStartAsync(ct);

        // Then 返回可处理的错误，而不是把异常抛给调度器。
        Assert.True(result.IsErr);
        if (result.IsErr)
            Assert.Contains("配置", result.Error.Message);
    }

    [Fact]
    public async Task Second_start_is_rejected_while_a_sync_is_running()
    {
        // Given 上一轮同步仍在读取店铺列表。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var erp = new TestErpClient
        {
            GetAllShops = async (_, _, _) =>
            {
                await gate.Task;
                return OpResults.Ok<IReadOnlyList<ErpShop>>([]);
            }
        };
        var (job, notifier, provider) = await CreateJobAsync(erp, ct);
        using var providerLifetime = provider;

        // When 在运行中再次触发。
        Assert.False((await job.TryStartAsync(ct)).IsErr);
        await WaitUntilAsync(() => job.Status.Running, "同步未进入运行中", ct);
        var second = await job.TryStartAsync(ct);

        // Then 第二次被拒绝；释放后第一轮正常结束。
        Assert.True(second.IsErr);
        gate.SetResult();
        var final = await WaitForIdleAsync(job, notifier, ct);
        Assert.False(final.Running);
        Assert.Equal(0, final.Total);
    }

    [Fact]
    public async Task Unconfigured_token_is_rejected_without_writing_state()
    {
        // Given 未配置私有 ERP AccessToken。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var (job, notifier, provider) = await CreateJobAsync(new TestErpClient(), ct, new TestErpTokenStore());
        using var providerLifetime = provider;

        // When 手动触发同步。
        var result = await job.TryStartAsync(ct);

        // Then 拒绝且不启动任务、不写状态。
        Assert.True(result.IsErr);
        if (result.IsErr)
            Assert.Contains("AccessToken", result.Error.Message);
        Assert.Empty(notifier.Statuses);
        await using var db = CreateDbContext();
        Assert.Empty(await db.ShopSyncStates.AsNoTracking().ToArrayAsync(ct));
    }

    private async Task<(ShopSyncJob Job, RecordingNotifier Notifier, ServiceProvider Provider)> CreateJobAsync(
        TestErpClient erp, CancellationToken cancellationToken, IErpTokenStore? store = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseOpenErpPostgres(_connectionString));
        services.AddSingleton<IErpClient>(erp);
        services.AddSingleton<IErpTokenStore>(store ?? new TestErpTokenStore("test-token"));
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var notifier = new RecordingNotifier();
        var job = new ShopSyncJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            notifier,
            TimeProvider.System,
            provider.GetRequiredService<ILogger<ShopSyncJob>>());
        await Task.CompletedTask;
        return (job, notifier, provider);
    }

    private static DateTimeOffset ToDatabasePrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % 10));

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync(cancellationToken);
    }

    private async Task SeedAsync(CancellationToken cancellationToken, DateTimeOffset? completedAt = null)
    {
        var finishedAt = completedAt ?? DateTimeOffset.UtcNow.AddMinutes(-10);
        await using var db = CreateDbContext();
        db.Shops.Add(new Shop
        {
            Sid = 30, Name = "旧店铺", Platform = "TemuPop", Region = "US",
            IsTokenOk = false, SyncedAt = finishedAt
        });
        db.Shops.Add(new Shop
        {
            Sid = 99, Name = "已不可见店铺", Platform = "TemuPop", IsTokenOk = true, SyncedAt = finishedAt
        });
        db.ShopPlatforms.Add(new ShopPlatform
        {
            Name = "OldPlatform", DisplayName = "旧平台", IsProduction = true
        });
        db.ShopSyncStates.Add(new ShopSyncState
        {
            Version = 7, LastCompletedAt = finishedAt, Total = 2, Valid = 1, Invalid = 1, Unknown = 0
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ShopSyncStatus> WaitForIdleAsync(
        ShopSyncJob job, RecordingNotifier notifier, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            var final = notifier.Statuses.LastOrDefault(status => !status.Running);
            if (final is not null && !job.Status.Running)
                return final;
            if (DateTime.UtcNow > deadline)
                throw new XunitException($"同步未在期限内结束：{job.Status}");
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string failure, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new XunitException(failure);
            await Task.Delay(10, cancellationToken);
        }
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseOpenErpPostgres(_connectionString).Options);

    private sealed class RecordingNotifier : IShopSyncNotifier
    {
        private readonly ConcurrentQueue<ShopSyncStatus> _statuses = new();

        public IReadOnlyList<ShopSyncStatus> Statuses => _statuses.ToArray();

        public Task PublishAsync(ShopSyncStatus status, CancellationToken cancellationToken)
        {
            _statuses.Enqueue(status);
            return Task.CompletedTask;
        }
    }
}
