using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders.Sources;

namespace OpenERP.Api.Orders;

/// <summary>店铺最近一次手动或定时同步的进度和结果。</summary>
public sealed record OrderSyncStatus(string Id, int Sid, string Status, string Stage, string Epoch, long Version,
    int Total = 0, int Read = 0, int Processed = 0, int Added = 0, int Updated = 0,
    int Unchanged = 0, int Skipped = 0, IReadOnlyDictionary<string, int>? SkipReasons = null, string? Error = null);

public sealed record OrderSyncSnapshot(string Epoch, IReadOnlyList<OrderSyncStatus> Statuses);

public sealed record OrderSyncStartResult(bool Accepted, OrderSyncStatus? Status, string? Error);

/// <summary>并行执行不同店铺的同步，同一店铺只运行一个任务。</summary>
public sealed class OrderSyncJobs(IServiceScopeFactory scopeFactory, ILogger<OrderSyncJobs> logger,
    IHubContext<ErpHub>? hub = null) : BackgroundService
{
    private const string Conflict = "已有店铺正在同步，请稍后重试";
    private const string Stopping = "应用正在停止，可在重启后手动重试。";
    private const int MaxConcurrentShops = 4;
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly Dictionary<int, OrderSyncStatus> _latest = [];
    private readonly HashSet<int> _activeSids = [];
    private readonly Channel<SyncWorkItem> _queue = Channel.CreateUnbounded<SyncWorkItem>();
    private readonly Channel<SyncNotification> _notifications = Channel.CreateUnbounded<SyncNotification>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<int> _pendingProgress = [];
    private readonly SemaphoreSlim _saving = new(1, 1);
    private readonly string _epoch = Guid.NewGuid().ToString("N");
    private long _version;
    private int _scheduledRemaining;
    private bool _stopping;

    public OpResult<OrderSyncStatus> Start(int sid)
    {
        lock (_gate)
            return StartLocked(sid);
    }

    /// <summary>受理多个店铺的手动任务，返回受理和跳过的店铺数。</summary>
    internal OpResult<(int StartedCount, int BusyCount)> StartAll(IReadOnlyList<int> sids)
    {
        lock (_gate)
        {
            if (_stopping)
                return OpResults.Err(Stopping);

            var started = 0;
            var busy = 0;
            foreach (var sid in sids.Distinct())
            {
                if (_activeSids.Contains(sid))
                {
                    busy++;
                    continue;
                }

                EnqueueLocked(sid, scheduled: false);
                started++;
            }

            return (started, busy);
        }
    }

    /// <summary>并行受理本轮空闲店铺，同一时间只运行一轮定时批次。</summary>
    public OpResult<int> StartScheduledBatch(IReadOnlyList<int> sids)
    {
        var distinct = sids.Distinct().ToArray();
        if (distinct.Length == 0)
            return 0;

        lock (_gate)
        {
            if (_stopping)
                return OpResults.Err(Stopping);
            if (_scheduledRemaining > 0)
                return OpResults.Err(Conflict);

            var available = distinct.Where(sid => !_activeSids.Contains(sid)).ToArray();
            if (available.Length == 0)
                return OpResults.Err(Conflict);

            _scheduledRemaining = available.Length;
            foreach (var sid in available)
                EnqueueLocked(sid, scheduled: true);
            return available.Length;
        }
    }

    private OpResult<OrderSyncStatus> StartLocked(int sid)
    {
        if (_stopping)
            return OpResults.Err(Stopping);
        if (_activeSids.Contains(sid))
            return OpResults.Err(Conflict);

        return EnqueueLocked(sid, scheduled: false);
    }

    private OrderSyncStatus EnqueueLocked(int sid, bool scheduled)
    {
        _activeSids.Add(sid);
        var job = SetLatestLocked(NewStatus(sid));
        // 写入和关闭都持有同一把锁，停止受理前队列始终可写。
        _queue.Writer.TryWrite(new SyncWorkItem(job, scheduled));
        return job;
    }

    public OrderSyncStatus? Latest(int sid)
    {
        lock (_gate)
            return _latest.GetValueOrDefault(sid);
    }

    public OrderSyncSnapshot Snapshot()
    {
        lock (_gate)
            return new(_epoch, _latest.Values.ToArray());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stopAccepting = stoppingToken.Register(() =>
        {
            lock (_gate)
            {
                _stopping = true;
                _queue.Writer.TryComplete();
            }
        });
        var notifications = SendNotificationsAsync(stoppingToken);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, MaxConcurrentShops).Select(_ => RunWorkerAsync(stoppingToken)));
        }
        finally
        {
            _notifications.Writer.TryComplete();
            await notifications;
        }
    }

    private async Task RunWorkerAsync(CancellationToken ct)
    {
        // 关闭队列后仍取出已受理的工作，把尚未开始的任务标为失败。
        await foreach (var work in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await SyncAsync(work.Job, ct);
            }
            catch (Exception error)
            {
                logger.LogError("同步工作项异常结束 {ErrorType}", error.GetType().Name);
            }
            finally
            {
                lock (_gate)
                {
                    _activeSids.Remove(work.Job.Sid);
                    if (work.Scheduled)
                        _scheduledRemaining--;
                }
            }
        }
    }

    private async Task SyncAsync(OrderSyncStatus job, CancellationToken ct)
    {
        var current = job;
        try
        {
            ct.ThrowIfCancellationRequested();
            current = job with { Stage = "shop" };
            SaveStatus(current);
            await using var scope = scopeFactory.CreateAsyncScope();
            var shops = scope.ServiceProvider.GetRequiredService<IPlatformShops>();
            var found = await shops.FindAsync(job.Sid, ct);
            if (found.IsErr)
            {
                Fail(current, found.Error.Message);
                return;
            }

            var shop = found.Value;
            var source = scope.ServiceProvider.GetRequiredService<PlatformOrderSources>().Find(shop.Platform);
            if (source is null)
            {
                Fail(current, $"暂不支持 {shop.Platform} 平台的订单同步。");
                return;
            }

            var read = await source.ReadOrdersAsync(shop, progress =>
            {
                current = current with
                {
                    Stage = progress.Stage, Total = progress.Total,
                    Read = progress.Read, Processed = progress.Processed
                };
                SaveStatus(current, progress: true);
            }, ct);

            if (read.IsErr)
            {
                Fail(current, read.Error.Message);
                return;
            }

            var batch = read.Value;
            var skipped = batch.SkipReasons.Values.Sum();
            current = current with { Stage = "saving", Skipped = skipped, SkipReasons = batch.SkipReasons };
            SaveStatus(current);

            var save = await SaveOrdersAsync(scope.ServiceProvider.GetRequiredService<OrderStore>(), batch.Orders, ct);
            if (save.IsErr)
            {
                Fail(current, save.Error.Message);
                return;
            }

            var counts = save.Value;
            SaveStatus(current with
            {
                Status = skipped == 0 ? "succeeded" : "partial",
                Stage = "done",
                Added = counts.Added,
                Updated = counts.Updated,
                Unchanged = counts.Unchanged
            });
            logger.LogInformation("订单同步完成 {JobId} {Sid}，读取 {Read}，新增 {Added}，更新 {Updated}，未变 {Unchanged}，跳过 {Skipped}",
                job.Id, job.Sid, current.Read, counts.Added, counts.Updated, counts.Unchanged, skipped);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Fail(current, Stopping);
        }
        catch (Exception error)
        {
            logger.LogError("订单同步 {JobId} {Sid} 在 {Stage} 阶段发生异常 {ErrorType}",
                job.Id, job.Sid, current.Stage, error.GetType().Name);
            Fail(current, current.Stage == "saving" ? "保存订单失败，请检查数据库后重试。" : "读取订单失败，请重试。");
        }
    }

    private async Task<OpResult<OrderSaveCounts>> SaveOrdersAsync(
        OrderStore store, IReadOnlyList<OrderSnapshot> orders, CancellationToken ct)
    {
        // 订单标识是全局唯一键，将查询已有记录和写入作为一个互斥区段。
        await _saving.WaitAsync(ct);
        try
        {
            return await store.SaveAsync(orders, ct);
        }
        finally
        {
            _saving.Release();
        }
    }

    private void Fail(OrderSyncStatus current, string message)
    {
        SaveStatus(current with { Status = "failed", Error = message });
        logger.LogWarning("订单同步失败 {JobId} {Sid} {Stage} {Reason}", current.Id, current.Sid, current.Stage, message);
    }

    private void SaveStatus(OrderSyncStatus job, bool progress = false)
    {
        lock (_gate)
            SetLatestLocked(job, progress);
    }

    private OrderSyncStatus SetLatestLocked(OrderSyncStatus job, bool progress = false)
    {
        var versioned = job with { Version = ++_version };
        _latest[job.Sid] = versioned;
        // 阶段和终态按版本发布。详情逐单更新快照，每店最多积压一条进度通知。
        if (hub is not null && (!progress || _pendingProgress.Add(job.Sid)))
            _notifications.Writer.TryWrite(new SyncNotification(versioned, progress));

        return versioned;
    }

    private async Task SendNotificationsAsync(CancellationToken ct)
    {
        if (hub is null)
            return;

        await foreach (var notification in _notifications.Reader.ReadAllAsync())
        {
            var status = notification.Status;
            if (notification.Progress)
            {
                lock (_gate)
                    _pendingProgress.Remove(status.Sid);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(NotificationTimeout);
            try
            {
                await hub.Clients.Groups([ErpHub.AdministratorGroup, ErpHub.OrderSyncGroup(status.Sid)])
                    .SendAsync(ErpHub.OrderSyncUpdated, status, timeout.Token);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "推送订单同步状态失败。");
            }
        }
    }

    private OrderSyncStatus NewStatus(int sid) =>
        new(Guid.NewGuid().ToString("N"), sid, "running", "queued", _epoch, 0, SkipReasons: new Dictionary<string, int>());

    private sealed record SyncWorkItem(OrderSyncStatus Job, bool Scheduled);
    private readonly record struct SyncNotification(OrderSyncStatus Status, bool Progress);
}
