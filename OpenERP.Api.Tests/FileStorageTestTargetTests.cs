using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class FileStorageTestTargetTests
{
    [Fact]
    public async Task Waits_for_storage_and_then_the_API_after_restarting_SeaweedFS()
    {
        // Given SeaweedFS 已健康，重启后将依次报告新一轮启动、存储健康和 API 健康。
        var ct = TestContext.Current.CancellationToken;
        var storage = new ContainerResource("seaweedfs");
        var startedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldStart = CreateStorageEvent(KnownResourceStates.Running, startedAt);
        var newStart = CreateStorageEvent(KnownResourceStates.Running, startedAt.AddSeconds(1));
        var commands = new List<string>();
        var storageReady = new TaskCompletionSource<ResourceEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiWaitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiReady = new TaskCompletionSource<ResourceEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        // When 执行真实集成测试使用的重启流程。
        var restart = AspireIntegrationFixture.RestartStorageAsync(
            (name, command, _) =>
            {
                commands.Add($"{command} {name}");
                return Task.FromResult(new ExecuteCommandResult { Success = true });
            },
            (name, matches, _) =>
            {
                commands.Add($"wait new start {name}");
                Assert.False(matches(oldStart));
                Assert.False(matches(CreateStorageEvent(KnownResourceStates.Starting, startedAt.AddSeconds(1))));
                Assert.True(matches(newStart));
                return Task.FromResult(newStart);
            },
            (name, _) =>
            {
                commands.Add($"wait healthy {name}");
                if (name == "api")
                {
                    apiWaitStarted.SetResult();
                    return apiReady.Task;
                }

                return commands.Count == 1 ? Task.FromResult(oldStart) : storageReady.Task;
            }, ct);

        // Then 存储健康前不等待 API，API 健康前不结束重启流程。
        Assert.Equal(["wait healthy seaweedfs", $"{KnownResourceCommands.RestartCommand} seaweedfs",
            "wait new start seaweedfs", "wait healthy seaweedfs"], commands);
        Assert.False(restart.IsCompleted);
        storageReady.SetResult(newStart);

        var next = await Task.WhenAny(restart, apiWaitStarted.Task).WaitAsync(ct);
        if (next == restart)
            await restart;
        Assert.Same(apiWaitStarted.Task, next);
        Assert.Equal("wait healthy api", commands[^1]);
        Assert.False(restart.IsCompleted);
        apiReady.SetResult(newStart);
        await restart;

        ResourceEvent CreateStorageEvent(string state, DateTime startTime) => new(storage, storage.Name,
            new CustomResourceSnapshot
            {
                ResourceType = "Container",
                Properties = [],
                State = new ResourceStateSnapshot(state, Style: null),
                StartTimeStamp = startTime
            });
    }
}
