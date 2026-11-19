using System.Threading.Channels;
using OpResult;
using OpenERP.Api.Erp;

namespace OpenERP.Api.Orders;

/// <summary>店铺最近一次手动或定时同步的进度和结果。</summary>
public sealed record OrderSyncStatus(string Id, int Sid, string Status, string Stage,
    int Total = 0, int Read = 0, int Processed = 0, int Added = 0, int Updated = 0,
    int Unchanged = 0, int Skipped = 0, IReadOnlyDictionary<string, int>? SkipReasons = null, string? Error = null);

/// <summary>串行执行手动单店同步或定时多店批次；全局同时只运行一个工作项。</summary>
public sealed class OrderSyncJobs(IServiceScopeFactory scopeFactory, ILogger<OrderSyncJobs> logger) : BackgroundService
{
    private const string Conflict = "已有店铺正在同步，请稍后重试";

    private readonly object _gate = new();
    private readonly Dictionary<int, OrderSyncStatus> _latest = [];
    private readonly Channel<SyncWorkItem> _queue = Channel.CreateBounded<SyncWorkItem>(1);
    private bool _active;

    public OpResult<OrderSyncStatus> Start(int sid)
    {
        lock (_gate)
        {
            if (_active)
                return OpResults.Err(Conflict);

            var job = NewStatus(sid);
            if (!_queue.Writer.TryWrite(new SyncWorkItem([sid], Scheduled: false)))
                return OpResults.Err(Conflict);

            _latest[sid] = job;
            _active = true;
            return job;
        }
    }

    /// <summary>受理一轮定时批次；已有任务时返回冲突，空选店不占用全局槽。</summary>
    public OpResult<int> StartScheduledBatch(IReadOnlyList<int> sids)
    {
        var distinct = sids.Distinct().ToArray();
        if (distinct.Length == 0)
            return 0;

        lock (_gate)
        {
            if (_active)
                return OpResults.Err(Conflict);
            if (!_queue.Writer.TryWrite(new SyncWorkItem(distinct, Scheduled: true)))
                return OpResults.Err(Conflict);

            foreach (var sid in distinct)
                _latest[sid] = NewStatus(sid);
            _active = true;
            return distinct.Length;
        }
    }

    public OrderSyncStatus? Latest(int sid)
    {
        lock (_gate)
            return _latest.GetValueOrDefault(sid);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunAsync(work, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                // 防御性兜底：消费循环不能因单个工作项的意外异常永久停止。
                logger.LogError("同步工作项异常结束 {ErrorType}", error.GetType().Name);
            }
        }
    }

    private async Task RunAsync(SyncWorkItem work, CancellationToken ct)
    {
        try
        {
            if (work.Scheduled)
                logger.LogInformation("开始定时同步批次，共 {Count} 家店铺。", work.Sids.Count);

            for (var index = 0; index < work.Sids.Count; index++)
            {
                if (ct.IsCancellationRequested)
                {
                    foreach (var pendingSid in work.Sids.Skip(index))
                        if (Latest(pendingSid) is { } pending)
                            Fail(pending, "应用正在停止，可在重启后手动重试。");
                    break;
                }

                if (Latest(work.Sids[index]) is { } job)
                    await SyncAsync(job, ct);
            }

            if (work.Scheduled)
                logger.LogInformation("定时同步批次结束，共 {Count} 家店铺。", work.Sids.Count);
        }
        catch (Exception error)
        {
            logger.LogError("同步批次异常结束 {ErrorType}", error.GetType().Name);
            foreach (var sid in work.Sids)
                if (Latest(sid) is { Status: "running" } pending)
                    Fail(pending, "同步批次异常结束，请重试。");
        }
        finally
        {
            lock (_gate)
                _active = false;
        }
    }

    private async Task SyncAsync(OrderSyncStatus job, CancellationToken ct)
    {
        var current = job;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tokens = scope.ServiceProvider.GetRequiredService<IErpTokenStore>();
            var token = await tokens.GetAsync(ct);
            if (token.IsErr)
            {
                Fail(current, token.Error.Message);
                return;
            }

            var erp = scope.ServiceProvider.GetRequiredService<IErpClient>();
            var read = await erp.ReadOrdersAsync(job.Sid, progress =>
            {
                current = current with
                {
                    Stage = progress.Stage, Total = progress.Total,
                    Read = progress.Read, Processed = progress.Processed
                };
                Publish(current);
            }, token.Value, ct);

            if (read.IsErr)
            {
                Fail(current, read.Error.Message);
                return;
            }

            var batch = read.Value;
            var skipped = batch.SkipReasons.Values.Sum();
            current = current with { Stage = "saving", Skipped = skipped, SkipReasons = batch.SkipReasons };
            Publish(current);

            var save = await scope.ServiceProvider.GetRequiredService<OrderStore>().SaveAsync(batch.Orders, ct);
            if (save.IsErr)
            {
                Fail(current, save.Error.Message);
                return;
            }

            var counts = save.Value;
            Publish(current with
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
            Fail(current, "应用正在停止，可在重启后手动重试。");
        }
        catch (Exception error)
        {
            logger.LogError("订单同步 {JobId} {Sid} 在 {Stage} 阶段发生异常 {ErrorType}",
                job.Id, job.Sid, current.Stage, error.GetType().Name);
            Fail(current, current.Stage == "saving" ? "保存订单失败，请检查数据库后重试。" : "读取订单失败，请重试。");
        }
    }

    private void Fail(OrderSyncStatus current, string message)
    {
        Publish(current with { Status = "failed", Error = message });
        logger.LogWarning("订单同步失败 {JobId} {Sid} {Stage} {Reason}", current.Id, current.Sid, current.Stage, message);
    }

    private void Publish(OrderSyncStatus job)
    {
        lock (_gate)
            _latest[job.Sid] = job;
    }

    private static OrderSyncStatus NewStatus(int sid) =>
        new(Guid.NewGuid().ToString("N"), sid, "running", "shop", SkipReasons: new Dictionary<string, int>());

    private sealed record SyncWorkItem(IReadOnlyList<int> Sids, bool Scheduled);
}
