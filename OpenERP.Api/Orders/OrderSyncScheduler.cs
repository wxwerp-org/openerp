using Microsoft.Extensions.Options;

namespace OpenERP.Api.Orders;

/// <summary>一轮定时同步的处理结果，用于日志与验收。</summary>
public enum OrderSyncCycleOutcome
{
    Started,
    Busy,
    Disabled,
    NoShops
}

/// <summary>定时同步周期配置；仅由服务端配置，不由 GUI 修改。</summary>
public sealed class OrderSyncSchedulerOptions
{
    public const int DefaultIntervalMinutes = 30;
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 1440;

    public int SchedulerIntervalMinutes { get; set; } = DefaultIntervalMinutes;
}

/// <summary>按固定周期为选中的店铺排队同步，配置保存后可动态唤醒重排。</summary>
public sealed class OrderSyncScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrderSyncJobs _jobs;
    private readonly TimeSpan _interval;
    private readonly ILogger<OrderSyncScheduler> _logger;
    private readonly SemaphoreSlim _settingsChanged = new(0, 1);
    private readonly object _gate = new();

    public OrderSyncScheduler(
        IServiceScopeFactory scopeFactory,
        OrderSyncJobs jobs,
        IOptions<OrderSyncSchedulerOptions> options,
        ILogger<OrderSyncScheduler> logger)
        : this(scopeFactory, jobs, TimeSpan.FromMinutes(options.Value.SchedulerIntervalMinutes), logger)
    {
    }

    internal OrderSyncScheduler(
        IServiceScopeFactory scopeFactory,
        OrderSyncJobs jobs,
        TimeSpan interval,
        ILogger<OrderSyncScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _jobs = jobs;
        _interval = interval;
        _logger = logger;
    }

    /// <summary>配置保存后唤醒调度循环，使新的启用状态和店铺选择无需重启即可生效。</summary>
    public void NotifySettingsChanged()
    {
        lock (_gate)
        {
            if (_settingsChanged.CurrentCount == 0)
                _settingsChanged.Release();
        }
    }

    /// <summary>执行一轮定时同步；返回本轮是受理、跳过还是无任务。</summary>
    public async Task<OrderSyncCycleOutcome> RunCycleAsync(CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        if (!settings.Enabled)
        {
            _logger.LogDebug("定时同步已关闭，跳过本轮。");
            return OrderSyncCycleOutcome.Disabled;
        }

        if (settings.ShopSids.Count == 0)
        {
            _logger.LogWarning("定时同步已启用但未选择店铺，本轮不同步。");
            return OrderSyncCycleOutcome.NoShops;
        }

        var started = _jobs.StartScheduledBatch(settings.ShopSids);
        if (started.IsErr)
        {
            _logger.LogInformation("定时同步本轮跳过：{Reason}；等待下一周期。", started.Error.Message);
            return OrderSyncCycleOutcome.Busy;
        }

        _logger.LogInformation("定时同步已受理 {Count} 家店铺：{ShopSids}。",
            started.Value, string.Join(",", settings.ShopSids));
        return OrderSyncCycleOutcome.Started;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("定时同步调度器已启动，周期 {IntervalMinutes} 分钟。", _interval.TotalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await LoadSettingsAsync(stoppingToken);
                if (!settings.Enabled || settings.ShopSids.Count == 0)
                {
                    // 关闭或无任务时不轮询数据库，等待配置保存后唤醒。
                    await WaitAsync(Timeout.InfiniteTimeSpan, stoppingToken);
                    continue;
                }

                if (await WaitAsync(_interval, stoppingToken))
                    continue;

                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                _logger.LogError("定时同步本轮执行失败，将在下一周期重试，异常类型 {ErrorType}。", error.GetType().Name);
                await WaitAsync(_interval, stoppingToken);
            }
        }
    }

    private async Task<OrderSyncSettingsSnapshot> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOrderSyncSettingsStore>().GetAsync(cancellationToken);
    }

    /// <summary>等待配置变更或超时；返回 true 表示配置已变更，应重新读取。</summary>
    private async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var wakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var wake = _settingsChanged.WaitAsync(wakeCancellation.Token);
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            await wake;
            return true;
        }

        var elapsed = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(wake, elapsed);
        wakeCancellation.Cancel();
        try
        {
            await wake;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 超时胜出时取消等待，避免遗留信号消费者。
        }

        cancellationToken.ThrowIfCancellationRequested();
        return completed == wake;
    }
}
