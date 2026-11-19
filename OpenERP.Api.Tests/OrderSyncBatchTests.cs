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

    [Fact]
    public async Task Schedule_continues_after_a_shop_fails_and_releases_the_global_slot()
    {
        // Given 定时批次包含两家店，第一家读取时更换 Token，随后因店铺不可见失败。
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = new TestErpTokenStore("first-token");
        var suppliedTokens = new List<string>();
        var erp = new TestErpClient
        {
            ReadOrders = async (sid, token, ct) =>
            {
                suppliedTokens.Add(token.Value);
                if (sid == 30)
                {
                    var saved = await tokens.SaveAsync("second-token", ct);
                    if (saved.IsErr)
                        return saved.Error;
                    return OpResults.Err("当前 ERP AccessToken 无法访问该 TemuPop 店铺。");
                }

                return new OrderImportBatch(0, [], new Dictionary<string, int>());
            }
        };
        using var provider = BuildProvider(erp, tokens);
        using var jobs = CreateJobs(provider);
        var started = jobs.StartScheduledBatch([30, 31]);
        if (started.IsErr)
            throw new XunitException(started.Error.Message);
        Assert.Equal(2, started.Value);

        try
        {
            // When 运行批次并等待两家店都结束。
            await jobs.StartAsync(cancellationToken);
            await TestWait.UntilAsync(
                () => AtTerminal(jobs, 30) && AtTerminal(jobs, 31),
                "定时批次没有在超时内结束。", cancellationToken);

            // Then 每家店读取一次，第二家用新 Token 完成，批次结束后可再次手动同步。
            Assert.Equal("failed", jobs.Latest(30)?.Status);
            Assert.Contains("无法访问", jobs.Latest(30)?.Error ?? "");
            Assert.Equal("succeeded", jobs.Latest(31)?.Status);
            Assert.Equal(2, tokens.Reads);
            Assert.Equal(["first-token", "second-token"], suppliedTokens);
            await TestWait.EventuallyAsync(() => jobs.Start(99), "批次结束后仍无法手动同步。", cancellationToken);
        }
        finally
        {
            await jobs.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Schedule_keeps_manual_starts_conflicted_until_all_shops_finish()
    {
        // Given 定时批次的第一家店已失败，第二家店仍在读取。
        var cancellationToken = TestContext.Current.CancellationToken;
        var secondShopStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondShop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var erp = new TestErpClient
        {
            ReadOrders = async (sid, _, ct) =>
            {
                if (sid == 30)
                    return OpResults.Err("店铺 30 读取失败。");

                secondShopStarted.TrySetResult();
                await releaseSecondShop.Task.WaitAsync(ct);
                return OpResults.Ok(new OrderImportBatch(0, [], new Dictionary<string, int>()));
            }
        };
        using var provider = BuildProvider(erp);
        using var jobs = CreateJobs(provider);
        try
        {
            var started = jobs.StartScheduledBatch([30, 31]);
            if (started.IsErr)
                throw new XunitException(started.Error.Message);
            await jobs.StartAsync(cancellationToken);
            await secondShopStarted.Task.WaitAsync(cancellationToken);
            await TestWait.UntilAsync(
                () => jobs.Latest(30)?.Status == "failed",
                "第一家店没有在超时内失败。", cancellationToken);

            // When 第一家店已经失败、第二家店仍在运行时手动提交另一家店。
            var manual = jobs.Start(99);

            // Then 全局单任务互斥保持到整个批次结束。
            Assert.True(manual.IsErr, "定时批次结束前必须拒绝手动同步。");
            releaseSecondShop.SetResult();
            await TestWait.UntilAsync(
                () => AtTerminal(jobs, 31),
                "定时批次没有在超时内结束。", cancellationToken);
            await TestWait.EventuallyAsync(() => jobs.Start(99), "批次结束后仍无法手动同步。", cancellationToken);
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

        // Then 空批次不占用全局槽，重复店铺只入队一次。
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
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql().Options;
        services.AddScoped(_ => new OrderStore(new AppDbContext(options)));
        return services.BuildServiceProvider();
    }
}
