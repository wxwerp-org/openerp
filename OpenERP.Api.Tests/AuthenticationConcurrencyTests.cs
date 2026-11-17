using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AuthenticationConcurrencyTests
{
    [Fact]
    public async Task Release_failure_preserves_both_errors_and_observes_all_operations()
    {
        // Given 锁等待已失败，一个请求已收到响应，另一个操作仍在运行。
        using var operations = new CancellationTokenSource();
        using var content = new StringContent("test response");
        using var response = new HttpResponseMessage { Content = content };
        var successful = Task.FromResult(response);
        var pending = Task.Delay(Timeout.InfiniteTimeSpan, operations.Token);
        var primary = new InvalidOperationException("lock observation failed");
        var releaseFailure = new InvalidOperationException("transaction release failed");
        bool? canceledDuringRelease = null;

        // When 释放事务也失败；Then 保留两个异常、观察取消，并释放已收到的响应。
        var error = await Assert.ThrowsAsync<AggregateException>(() =>
            AuthenticationConcurrency.ReleaseAsync(_ =>
            {
                canceledDuringRelease = operations.IsCancellationRequested;
                return Task.FromException(releaseFailure);
            }, operations, primary, TestContext.Current.CancellationToken, successful, pending));

        Assert.False(canceledDuringRelease);
        Assert.Collection(error.InnerExceptions, first => Assert.Same(primary, first),
            second => Assert.Same(releaseFailure, second));
        Assert.True(pending.IsCanceled);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Test_cancellation_reaps_operations_without_canceling_transaction_release(bool cancelBeforeRelease)
    {
        // Given 测试可在解锁前或解锁后取消，参与操作使用独立令牌以保证先解锁再收尾。
        using var testCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var operations = new CancellationTokenSource();
        var pending = Task.Delay(Timeout.InfiniteTimeSpan, operations.Token);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? releaseTokenCanceled = null;
        bool? operationsCanceledDuringRelease = null;
        if (cancelBeforeRelease)
            await testCancellation.CancelAsync();

        var release = AuthenticationConcurrency.ReleaseAsync(token =>
        {
            releaseTokenCanceled = token.IsCancellationRequested;
            operationsCanceledDuringRelease = operations.IsCancellationRequested;
            released.SetResult();
            return Task.CompletedTask;
        }, operations, null, testCancellation.Token, pending);
        try
        {
            await released.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            if (!cancelBeforeRelease)
                await testCancellation.CancelAsync();

            // When 正常等待收到测试取消；Then 及时取消并观察操作，保留测试取消语义。
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                release.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            Assert.Equal(testCancellation.Token, error.CancellationToken);
            Assert.False(releaseTokenCanceled);
            Assert.False(operationsCanceledDuringRelease);
            Assert.True(operations.IsCancellationRequested);
            Assert.True(pending.IsCanceled);
        }
        finally
        {
            // 红灯时同样回收探针任务，不能为验证取消而遗留挂起操作。
            await operations.CancelAsync();
            await Record.ExceptionAsync(async () => await release);
        }
    }

}
