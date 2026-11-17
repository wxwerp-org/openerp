using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpResult;
using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderSyncSchedulerTests
{
    [Fact]
    public async Task Enabled_cycle_queues_all_selected_shops_and_blocks_manual_sync()
    {
        // Given 定时同步已启用并选中两家店。
        var (scheduler, jobs, store, provider) = CreateScheduler();
        store.Settings = new(true, [30, 644928]);

        // When 调度器执行一轮。
        var outcome = await scheduler.RunCycleAsync(TestContext.Current.CancellationToken);

        // Then 两家店都进入运行状态，手动同步因全局互斥被拒绝。
        Assert.Equal(OrderSyncCycleOutcome.Started, outcome);
        Assert.Equal("running", jobs.Latest(30)?.Status);
        Assert.Equal("running", jobs.Latest(644928)?.Status);
        Assert.True(jobs.Start(31).IsErr);
        await DisposeAsync(provider, scheduler);
    }

    [Fact]
    public async Task Disabled_cycle_does_not_queue_any_work()
    {
        // Given 定时同步保持关闭。
        var (scheduler, jobs, store, provider) = CreateScheduler();
        store.Settings = new(false, [30]);

        // When 调度器执行一轮。
        var outcome = await scheduler.RunCycleAsync(TestContext.Current.CancellationToken);

        // Then 没有任务入队，手动同步仍可受理。
        Assert.Equal(OrderSyncCycleOutcome.Disabled, outcome);
        Assert.Null(jobs.Latest(30));
        Assert.False(jobs.Start(30).IsErr);
        await DisposeAsync(provider, scheduler);
    }

    [Fact]
    public async Task Enabled_cycle_without_shops_is_skipped()
    {
        // Given 定时同步已启用但没有选中店铺。
        var (scheduler, jobs, store, provider) = CreateScheduler();
        store.Settings = new(true, []);

        // When 调度器执行一轮。
        var outcome = await scheduler.RunCycleAsync(TestContext.Current.CancellationToken);

        // Then 明确跳过且不占用全局槽。
        Assert.Equal(OrderSyncCycleOutcome.NoShops, outcome);
        Assert.Null(jobs.Latest(30));
        Assert.False(jobs.Start(30).IsErr);
        await DisposeAsync(provider, scheduler);
    }

    [Fact]
    public async Task Manual_sync_in_progress_makes_the_cycle_skip_until_the_next_period()
    {
        // Given 手动同步正在进行。
        var (scheduler, jobs, store, provider) = CreateScheduler();
        var manual = jobs.Start(30);
        if (manual.IsErr)
            throw new Xunit.Sdk.XunitException(manual.Error.Message);
        store.Settings = new(true, [30, 644928]);

        // When 定时周期到来。
        var outcome = await scheduler.RunCycleAsync(TestContext.Current.CancellationToken);

        // Then 本轮直接跳过，不改变正在运行的手动任务。
        Assert.Equal(OrderSyncCycleOutcome.Busy, outcome);
        Assert.Equal(manual.Value.Id, jobs.Latest(30)?.Id);
        await DisposeAsync(provider, scheduler);
    }

    [Fact]
    public async Task Saving_settings_wakes_the_loop_and_the_first_cycle_waits_one_period()
    {
        // Given 定时同步关闭，调度循环以 300ms 周期运行。
        var cancellationToken = TestContext.Current.CancellationToken;
        var (scheduler, jobs, store, provider) = CreateScheduler(TimeSpan.FromMilliseconds(300));
        store.Settings = new(false, []);
        try
        {
            await scheduler.StartAsync(cancellationToken);
            await Task.Delay(100, cancellationToken);
            Assert.Null(jobs.Latest(30));

            // When 保存配置启用店铺并通知调度器。
            store.Settings = new(true, [30]);
            scheduler.NotifySettingsChanged();
            await Task.Delay(100, cancellationToken);

            // Then 不会立即执行，而是等待一个周期后才受理。
            Assert.Null(jobs.Latest(30));
            await TestWait.UntilAsync(
                () => jobs.Latest(30) is not null,
                "调度器没有在保存配置后按周期启动。", cancellationToken);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
            await DisposeAsync(provider, scheduler);
        }
    }

    [Fact]
    public async Task Transient_settings_failure_retries_after_one_period_not_two()
    {
        // Given 设置读取第一次失败，周期为 1 秒。
        var cancellationToken = TestContext.Current.CancellationToken;
        var (scheduler, jobs, store, provider) = CreateScheduler(TimeSpan.FromSeconds(1));
        store.Settings = new(true, [30]);
        store.FailuresRemaining = 1;
        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // When 启动调度循环，等待首次批次受理。
            await scheduler.StartAsync(cancellationToken);
            await TestWait.UntilAsync(
                () => jobs.Latest(30) is not null,
                "暂时性设置读取失败后没有重试。", cancellationToken);

            // Then 只等待一个周期，而不是异常路径再叠加一个周期。
            Assert.True(startedAt.Elapsed < TimeSpan.FromSeconds(1.7),
                $"异常后的首次重试耗时 {startedAt.Elapsed.TotalSeconds:F2}s，超过一个周期加余量。");
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
            await DisposeAsync(provider, scheduler);
        }
    }

    private static (OrderSyncScheduler Scheduler, OrderSyncJobs Jobs, FakeSettingsStore Store, ServiceProvider Provider)
        CreateScheduler(TimeSpan? interval = null)
    {
        var store = new FakeSettingsStore();
        var services = new ServiceCollection();
        services.AddSingleton<IOrderSyncSettingsStore>(store);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var jobs = new OrderSyncJobs(scopeFactory, NullLogger<OrderSyncJobs>.Instance);
        var scheduler = new OrderSyncScheduler(scopeFactory, jobs,
            interval ?? TimeSpan.FromMinutes(30), NullLogger<OrderSyncScheduler>.Instance);
        return (scheduler, jobs, store, provider);
    }

    private static async Task DisposeAsync(ServiceProvider provider, OrderSyncScheduler scheduler)
    {
        scheduler.Dispose();
        await provider.DisposeAsync();
    }

    private sealed class FakeSettingsStore : IOrderSyncSettingsStore
    {
        public OrderSyncSettingsSnapshot Settings { get; set; } = new(false, []);

        public int FailuresRemaining { get; set; }

        public Task<OrderSyncSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("transient settings failure");
            }

            return Task.FromResult(Settings);
        }

        public Task<OpResult<OrderSyncSettingsSnapshot>> SaveAsync(
            bool enabled, IReadOnlyList<int> shopSids, CancellationToken cancellationToken)
        {
            Settings = new(enabled, shopSids.Distinct().Order().ToArray());
            return Task.FromResult(OpResults.Ok(Settings));
        }
    }
}
