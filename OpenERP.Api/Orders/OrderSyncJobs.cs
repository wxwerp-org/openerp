using System.Threading.Channels;
using OpResult;
using OpenERP.Api.Erp;

namespace OpenERP.Api.Orders;

/// <summary>店铺最近一次手动同步的进度和结果。</summary>
public sealed record OrderSyncStatus(string Id, int Sid, string Status, string Stage,
    int Total = 0, int Read = 0, int Processed = 0, int Added = 0, int Updated = 0,
    int Unchanged = 0, int Skipped = 0, IReadOnlyDictionary<string, int>? SkipReasons = null, string? Error = null);

public sealed class OrderSyncJobs(IServiceScopeFactory scopeFactory, ILogger<OrderSyncJobs> logger) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Dictionary<int, OrderSyncStatus> _latest = [];
    private readonly Channel<OrderSyncStatus> _queue = Channel.CreateBounded<OrderSyncStatus>(1);
    private bool _active;

    public OpResult<OrderSyncStatus> Start(int sid)
    {
        lock (_gate)
        {
            const string conflict = "已有店铺正在同步，请稍后重试";
            if (_active)
                return OpResults.Err(conflict);

            var job = new OrderSyncStatus(Guid.NewGuid().ToString("N"), sid, "running", "shop",
                SkipReasons: new Dictionary<string, int>());
            if (!_queue.Writer.TryWrite(job))
                return OpResults.Err(conflict);

            _latest[sid] = job;
            _active = true;
            return job;
        }
    }

    public OrderSyncStatus? Latest(int sid)
    {
        lock (_gate)
            return _latest.GetValueOrDefault(sid);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            await SyncAsync(job, stoppingToken);
    }

    private async Task SyncAsync(OrderSyncStatus job, CancellationToken ct)
    {
        var current = job;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var erp = scope.ServiceProvider.GetRequiredService<WxwErpClient>();
            var read = await erp.ReadOrdersAsync(job.Sid, progress =>
            {
                current = current with
                {
                    Stage = progress.Stage, Total = progress.Total,
                    Read = progress.Read, Processed = progress.Processed
                };
                Publish(current);
            }, ct);

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
            }, finished: true);
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
        Publish(current with { Status = "failed", Error = message }, finished: true);
        logger.LogWarning("订单同步失败 {JobId} {Sid} {Stage} {Reason}", current.Id, current.Sid, current.Stage, message);
    }

    private void Publish(OrderSyncStatus job, bool finished = false)
    {
        lock (_gate)
        {
            _latest[job.Sid] = job;
            if (finished)
                _active = false;
        }
    }
}
