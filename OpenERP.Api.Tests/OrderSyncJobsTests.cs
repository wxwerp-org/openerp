using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class OrderSyncJobsTests
{
    [Fact]
    public void ActiveJobReturnsAConflictResultWithoutQueuingAnotherShop()
    {
        // Given 第一个同步任务已受理。
        using var services = new ServiceCollection().BuildServiceProvider();
        using var jobs = new OrderSyncJobs(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OrderSyncJobs>.Instance);
        var started = jobs.Start(30);
        if (started.IsErr)
            throw new XunitException(started.Error.Message);
        var first = started.Value;

        // When 另一个店铺同时提交。
        var result = jobs.Start(644928);

        // Then 返回可处理的冲突结果，第一项任务仍是最近一次，第二家店没有任务。
        if (!result.IsErr)
            throw new XunitException("并发提交必须以冲突结束。");
        Assert.Equal("已有店铺正在同步，请稍后重试", result.Error.Message);
        Assert.Equal(first, jobs.Latest(30));
        Assert.Null(jobs.Latest(644928));
    }
}
