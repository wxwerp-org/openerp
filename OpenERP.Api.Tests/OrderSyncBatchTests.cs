using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class OrderSyncBatchTests
{
    [Theory]
    [InlineData(false, "尚未配置私有 ERP AccessToken。")]
    [InlineData(true, "读取私有 ERP AccessToken 失败，请稍后重试。")]
    public async Task Sync_fails_at_shop_stage_when_token_cannot_be_read(bool readFails, string expectedError)
    {
        // Given 存储中没有 Token 或读取失败，且上游可以返回成功结果。
        var cancellationToken = TestContext.Current.CancellationToken;
        IErpTokenStore tokens = readFails ? new FailingErpTokenStore() : new TestErpTokenStore();
        var upstreamCalled = false;
        var erp = new TestErpClient
        {
            ReadOrders = (_, _, _) =>
            {
                upstreamCalled = true;
                return Task.FromResult(OpResults.Ok(new OrderImportBatch(0, [], new Dictionary<string, int>())));
            }
        };
        using var provider = BuildProvider(erp, tokens);
        using var jobs = CreateJobs(provider);

        try
        {
            // When 同步任务被受理，后台开始读取店铺订单。
            var started = jobs.Start(30);
            Assert.False(started.IsErr);
            await jobs.StartAsync(cancellationToken);
            await TestWait.UntilAsync(() => AtTerminal(jobs, 30), "同步没有在超时内结束。", cancellationToken);

            // Then 在读取店铺前结束，状态暴露可读错误，未导入订单。
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

    [Theory]
    [InlineData("manual")]
    [InlineData("scheduled")]
    [InlineData("mixed")]
    public async Task Different_shops_can_read_at_the_same_time_and_finish_independently(string source)
    {
        // Given 两店读取各自停在可控等待点，第二家会先因店铺不可见失败。
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstShopStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondShopStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstShop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondShop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new TestErpTokenStore("test-token");
        var erp = new TestErpClient
        {
            ReadOrders = async (sid, token, ct) =>
            {
                if (token.Value != "test-token")
                    return OpResults.Err("凭据不正确。");

                if (sid == 30)
                {
                    firstShopStarted.TrySetResult();
                    await releaseFirstShop.Task.WaitAsync(ct);
                    return new OrderImportBatch(0, [], new Dictionary<string, int>());
                }

                secondShopStarted.TrySetResult();
                await releaseSecondShop.Task.WaitAsync(ct);
                return OpResults.Err("当前 ERP AccessToken 无法访问该 TemuPop 店铺。");
            }
        };
        using var provider = BuildProvider(erp, tokens);
        using var jobs = CreateJobs(provider);

        try
        {
            // When 由手动、定时或混合来源分别发起两家店的任务。
            if (source == "scheduled")
                Assert.False(jobs.StartScheduledBatch([30, 31]).IsErr);
            else
            {
                Assert.False(jobs.Start(30).IsErr);
                if (source == "mixed")
                    Assert.False(jobs.StartScheduledBatch([30, 31]).IsErr);
                else
                    Assert.False(jobs.Start(31).IsErr);
            }

            await jobs.StartAsync(cancellationToken);
            await Task.WhenAll(firstShopStarted.Task, secondShopStarted.Task)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            // Then 第一家仍在读取时，第二家已进入读取。每店单独取凭据，且拒绝同店重复启动。
            var first = Assert.IsType<OrderSyncStatus>(jobs.Latest(30));
            var second = Assert.IsType<OrderSyncStatus>(jobs.Latest(31));
            Assert.Equal("running", first.Status);
            Assert.Equal("running", second.Status);
            Assert.Equal(2, tokens.Reads);
            Assert.True(jobs.Start(30).IsErr);
            Assert.True(jobs.Start(31).IsErr);

            releaseSecondShop.SetResult();
            await TestWait.UntilAsync(() => AtTerminal(jobs, 31), "第二家店未独立结束。", cancellationToken);
            Assert.Equal(first, jobs.Latest(30));
            Assert.Equal(second.Id, jobs.Latest(31)?.Id);
            Assert.Equal("failed", jobs.Latest(31)?.Status);
            Assert.Contains("无法访问", jobs.Latest(31)?.Error ?? "");

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
        var fifthShopStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseShops = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var tokens = new TestErpTokenStore("test-token");
        var erp = new TestErpClient
        {
            ReadOrders = async (_, token, ct) =>
            {
                switch (Interlocked.Increment(ref count))
                {
                    case 4:
                        fourShopsStarted.SetResult();
                        break;
                    case 5:
                        fifthShopStarted.SetResult(token.Value);
                        break;
                }

                await releaseShops.Task.WaitAsync(ct);
                return new OrderImportBatch(0, [], new Dictionary<string, int>());
            }
        };
        using var provider = BuildProvider(erp, tokens);
        using var jobs = CreateJobs(provider);
        Assert.False(jobs.Start(30).IsErr);
        Assert.False(jobs.StartScheduledBatch([31, 32, 33, 34]).IsErr);

        try
        {
            // When 开始处理任务，前四家进入读取。
            await jobs.StartAsync(cancellationToken);
            await fourShopsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            // Then 第五家已受理，但须等空出位置才读取凭据并访问 ERP。
            await Assert.ThrowsAsync<TimeoutException>(() =>
                fifthShopStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(200), cancellationToken));
            Assert.Equal(4, tokens.Reads);
            Assert.Equal("queued", jobs.Latest(34)?.Stage);

            // 等待期间凭据轮换，下一家店应在实际开始时读取新值。
            var saved = await tokens.SaveAsync("replacement-token", cancellationToken);
            Assert.False(saved.IsErr);
            releaseShops.SetResult();
            Assert.Equal("replacement-token",
                await fifthShopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
            Assert.Equal(5, tokens.Reads);
        }
        finally
        {
            await jobs.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Schedule_deduplicates_shops_and_ignores_an_empty_selection()
    {
        // Given 空选店与重复 SID 的定时请求。
        using var provider = BuildProvider(new TestErpClient());
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

    private static OrderSyncJobs CreateJobs(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OrderSyncJobs>.Instance);

    private static bool AtTerminal(OrderSyncJobs jobs, int sid) =>
        jobs.Latest(sid)?.Status is "succeeded" or "partial" or "failed";

    private static ServiceProvider BuildProvider(TestErpClient erp, IErpTokenStore? tokens = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IErpClient>(erp);
        services.AddSingleton(tokens ?? new TestErpTokenStore("test-token"));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseOpenErpPostgres().Options;
        services.AddScoped(_ => new OrderStore(new AppDbContext(options)));
        return services.BuildServiceProvider();
    }
}
