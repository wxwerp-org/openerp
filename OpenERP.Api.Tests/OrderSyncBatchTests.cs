using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpResult;
using OpenERP.Api.Orders;
using OpenERP.Api.Orders.Sources;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class OrderSyncBatchTests
{
    [Theory]
    [InlineData("店铺不存在或已从授权店铺中移除。")]
    public async Task Sync_fails_at_shop_stage_when_the_shop_cannot_be_found(string expectedError)
    {
        // Given 授权店铺目录里找不到该店铺，且平台可以返回成功结果。
        var cancellationToken = TestContext.Current.CancellationToken;
        var upstreamCalled = false;
        var source = new TestPlatformOrderSource((_, _, _) =>
        {
            upstreamCalled = true;
            return Task.FromResult(OpResults.Ok(new OrderImportBatch(0, [], new Dictionary<string, int>())));
        });
        using var provider = BuildProvider(source, new TestPlatformShops { Failure = expectedError });
        using var jobs = CreateJobs(provider);

        try
        {
            // When 同步任务被受理，后台开始读取店铺订单。
            var started = jobs.Start(30);
            Assert.False(started.IsErr);
            await jobs.StartAsync(cancellationToken);
            await TestWait.UntilAsync(() => AtTerminal(jobs, 30), "同步没有在超时内结束。", cancellationToken);

            // Then 在读取订单前结束，状态暴露可读错误，未导入订单。
            var status = Assert.IsType<OrderSyncStatus>(jobs.Latest(30));
            Assert.Equal("failed", status.Status);
            Assert.Equal("shop", status.Stage);
            Assert.Equal(expectedError, status.Error);
            Assert.Equal(0, status.Added);
            Assert.False(upstreamCalled);
        }
        finally
        {
            await jobs.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Sync_fails_at_shop_stage_when_the_platform_has_no_order_source()
    {
        // Given 店铺属于没有订单来源实现的平台。
        var cancellationToken = TestContext.Current.CancellationToken;
        var upstreamCalled = false;
        var source = new TestPlatformOrderSource((_, _, _) =>
        {
            upstreamCalled = true;
            return Task.FromResult(OpResults.Ok(new OrderImportBatch(0, [], new Dictionary<string, int>())));
        });
        using var provider = BuildProvider(source, new TestPlatformShops { Platform = "Unsupported" });
        using var jobs = CreateJobs(provider);

        try
        {
            // When 同步被受理。
            Assert.False(jobs.Start(30).IsErr);
            await jobs.StartAsync(cancellationToken);
            await TestWait.UntilAsync(() => AtTerminal(jobs, 30), "同步没有在超时内结束。", cancellationToken);

            // Then 任务以明确的平台不支持错误失败，没有调用任何来源。
            var status = Assert.IsType<OrderSyncStatus>(jobs.Latest(30));
            Assert.Equal("failed", status.Status);
            Assert.Contains("Unsupported", status.Error);
            Assert.False(upstreamCalled);
        }
        finally
        {
            await jobs.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("scheduled")]
    [InlineData("mixed")]
    public async Task Different_shops_can_read_at_the_same_time_and_finish_independently(string trigger)
    {
        // Given 两店读取各自停在可控等待点，第二家会先因平台拒绝失败。
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstShopStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondShopStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstShop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondShop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var credentials = new TestPlatformShops();
        var source = new TestPlatformOrderSource(async (shop, _, ct) =>
        {
            if (shop.Sid == 30)
            {
                firstShopStarted.TrySetResult();
                await releaseFirstShop.Task.WaitAsync(ct);
                return new OrderImportBatch(0, [], new Dictionary<string, int>());
            }

            secondShopStarted.TrySetResult();
            await releaseSecondShop.Task.WaitAsync(ct);
            return OpResults.Err("AliExpress 拒绝了该店铺的访问。");
        });
        using var provider = BuildProvider(source, credentials);
        using var jobs = CreateJobs(provider);

        try
        {
            // When 由手动、定时或混合来源分别发起两家店的任务。
            if (trigger == "scheduled")
                Assert.False(jobs.StartScheduledBatch([30, 31]).IsErr);
            else
            {
                Assert.False(jobs.Start(30).IsErr);
                if (trigger == "mixed")
                    Assert.False(jobs.StartScheduledBatch([30, 31]).IsErr);
                else
                    Assert.False(jobs.Start(31).IsErr);
            }

            await jobs.StartAsync(cancellationToken);
            await Task.WhenAll(firstShopStarted.Task, secondShopStarted.Task)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            // Then 第一家仍在读取时，第二家已进入读取。每店单独查找店铺，且拒绝同店重复启动。
            var first = Assert.IsType<OrderSyncStatus>(jobs.Latest(30));
            var second = Assert.IsType<OrderSyncStatus>(jobs.Latest(31));
            Assert.Equal("running", first.Status);
            Assert.Equal("running", second.Status);
            Assert.Equal(2, credentials.Reads);
            Assert.True(jobs.Start(30).IsErr);
            Assert.True(jobs.Start(31).IsErr);

            releaseSecondShop.SetResult();
            await TestWait.UntilAsync(() => AtTerminal(jobs, 31), "第二家店未独立结束。", cancellationToken);
            Assert.Equal(first, jobs.Latest(30));
            Assert.Equal(second.Id, jobs.Latest(31)?.Id);
            Assert.Equal("failed", jobs.Latest(31)?.Status);
            Assert.Contains("拒绝", jobs.Latest(31)?.Error ?? "");

            // 第二家店结束即可补跑，第一家店随后独立完成。
            var retried = await TestWait.EventuallyAsync(
                () => jobs.Start(31), "第二家店结束后仍无法补跑。", cancellationToken);
            Assert.NotEqual(second.Id, retried.Id);
            releaseFirstShop.SetResult();
            await TestWait.UntilAsync(() => AtTerminal(jobs, 30), "第一家店未完成。", cancellationToken);
            Assert.Equal("succeeded", jobs.Latest(30)?.Status);
        }
        finally
        {
            await jobs.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Manual_and_scheduled_work_share_four_read_slots()
    {
        // Given 手动和定时共受理五家店，每家进入读取后保持阻塞。
        var cancellationToken = TestContext.Current.CancellationToken;
        var fourShopsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fifthShopStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseShops = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var credentials = new TestPlatformShops();
        var source = new TestPlatformOrderSource(async (shop, _, ct) =>
        {
            switch (Interlocked.Increment(ref count))
            {
                case 4:
                    fourShopsStarted.SetResult();
                    break;
                case 5:
                    fifthShopStarted.SetResult(shop.Sid);
                    break;
            }

            await releaseShops.Task.WaitAsync(ct);
            return new OrderImportBatch(0, [], new Dictionary<string, int>());
        });
        using var provider = BuildProvider(source, credentials);
        using var jobs = CreateJobs(provider);
        Assert.False(jobs.Start(30).IsErr);
        Assert.False(jobs.StartScheduledBatch([31, 32, 33, 34]).IsErr);

        try
        {
            // When 开始处理任务，前四家进入读取。
            await jobs.StartAsync(cancellationToken);
            await fourShopsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            // Then 第五家已受理，但须等空出位置才查找店铺并访问平台。
            await Assert.ThrowsAsync<TimeoutException>(() =>
                fifthShopStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(200), cancellationToken));
            Assert.Equal(4, credentials.Reads);
            Assert.Equal("queued", jobs.Latest(34)?.Stage);

            // 空出位置后，第五家店才开始查找店铺并读取。
            releaseShops.SetResult();
            Assert.Equal(34, await fifthShopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
            Assert.Equal(5, credentials.Reads);
        }
        finally
        {
            await jobs.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Schedule_deduplicates_shops_and_ignores_an_empty_selection()
    {
        // Given 空选店与重复店铺编号的定时请求。
        using var provider = BuildProvider(new TestPlatformOrderSource(), new TestPlatformShops());
        using var jobs = CreateJobs(provider);

        // When 分别受理空批次和重复店铺。
        var empty = jobs.StartScheduledBatch([]);
        var duplicated = jobs.StartScheduledBatch([30, 30]);

        // Then 空批次不占用店铺，重复店铺只入队一次。
        if (empty.IsErr)
            throw new XunitException(empty.Error.Message);
        Assert.Equal(0, empty.Value);
        if (duplicated.IsErr)
            throw new XunitException(duplicated.Error.Message);
        Assert.Equal(1, duplicated.Value);
        Assert.Equal("running", jobs.Latest(30)?.Status);
    }

    internal static OrderSyncJobs CreateJobs(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OrderSyncJobs>.Instance);

    private static bool AtTerminal(OrderSyncJobs jobs, int sid) =>
        jobs.Latest(sid)?.Status is "succeeded" or "partial" or "failed";

    private static ServiceProvider BuildProvider(TestPlatformOrderSource source, TestPlatformShops credentials)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPlatformShops>(credentials);
        services.AddSingleton(new PlatformOrderSources([source]));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql().Options;
        services.AddScoped(_ => new OrderStore(new AppDbContext(options)));
        return services.BuildServiceProvider();
    }
}
