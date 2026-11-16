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
    [Fact]
    public async Task Schedule_continues_after_a_shop_fails_and_releases_the_global_slot()
    {
        // Given 定时批次包含两家店，第一家读取失败，第二家成功返回空批次。
        var cancellationToken = TestContext.Current.CancellationToken;
        var erp = new TestErpClient
        {
            ReadOrders = (sid, _) => Task.FromResult(sid == 30
                ? (OpResult<OrderImportBatch>)OpResults.Err("当前 ERP AccessToken 无法访问该 TemuPop 店铺。")
                : OpResults.Ok(new OrderImportBatch(0, [], new Dictionary<string, int>())))
        };
        using var provider = BuildProvider(erp);
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

            // Then 失败只记录在该店，另一家继续完成；批次结束后可再次手动同步。
            Assert.Equal("failed", jobs.Latest(30)?.Status);
            Assert.Contains("无法访问", jobs.Latest(30)?.Error ?? "");
            Assert.Equal("succeeded", jobs.Latest(31)?.Status);
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
            ReadOrders = async (sid, ct) =>
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

    private static ServiceProvider BuildProvider(TestErpClient erp)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IErpClient>(erp);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql().Options;
        services.AddScoped(_ => new OrderStore(new AppDbContext(options)));
        return services.BuildServiceProvider();
    }
}
