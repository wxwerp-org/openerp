using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class AspireResourceLifecycleTests
{
    [Fact]
    public async Task Initialize_starts_only_postgres_and_rejects_an_unprepared_api_client()
    {
        // Given 一个隔离 fixture；When 初始化 fixture；Then PostgreSQL 已就绪，API 及其余服务仍未启动。
        var fixture = new AspireIntegrationFixture(initializeAdmin: false);
        var cancellationToken = TestContext.Current.CancellationToken;
        string? secretsDirectory;
        await using (fixture)
        {
            await fixture.InitializeAsync();
            secretsDirectory = fixture.TestSecretsDirectoryPath;
            Assert.NotNull(secretsDirectory);
            var postgres = await fixture.GetResourceSnapshotAsync("postgres", cancellationToken);
            Assert.Equal(KnownResourceStates.Running, postgres.Snapshot.State?.Text);
            await AssertResourcesNotStartedAsync(fixture, ["api", "redis", "seaweedfs", "web"], cancellationToken);
            await AssertResourcesNotStartedAsync(fixture, fixture.FrontendInstallerResourceNames, cancellationToken);
            Assert.Throws<InvalidOperationException>(() => fixture.CreateApiClient());
        }

        // Then 即使从未启动存储，释放也会清理本次测试的密钥与专用卷。
        Assert.False(Directory.Exists(secretsDirectory));
        Assert.False(await fixture.TestStorageVolumeExistsAsync(cancellationToken));
    }

    [Fact]
    public async Task Storage_and_api_start_lazily_once_and_cleanup_their_test_resources()
    {
        var fixture = new AspireIntegrationFixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        var disposeAttempted = false;
        try
        {
            // Given 只有 PostgreSQL 已启动的 fixture。
            await fixture.InitializeAsync();
            var postgresBefore = await fixture.GetResourceSnapshotAsync("postgres", cancellationToken);
            await AssertResourcesNotStartedAsync(fixture, ["api", "redis", "seaweedfs", "web"], cancellationToken);
            await AssertResourcesNotStartedAsync(fixture, fixture.FrontendInstallerResourceNames, cancellationToken);

            // When 获取文件存储连接；Then 只启动 SeaweedFS，其它服务仍等待显式启动。
            var storageConnection = await fixture.GetStorageConnectionStringAsync(cancellationToken);
            Assert.False(string.IsNullOrWhiteSpace(storageConnection));
            var storageBeforeApi = await fixture.GetResourceSnapshotAsync("seaweedfs", cancellationToken);
            Assert.Equal(KnownResourceStates.Running, storageBeforeApi.Snapshot.State?.Text);
            await AssertResourcesNotStartedAsync(fixture, ["api", "redis", "web"], cancellationToken);
            await AssertResourcesNotStartedAsync(fixture, fixture.FrontendInstallerResourceNames, cancellationToken);

            // When 并发重复确保存储；Then 复用同一启动，不改变资源 identity 或 start 时间。
            await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => fixture.EnsureStorageStartedAsync(cancellationToken)));
            var storageAfterRepeatedEnsure = await fixture.GetResourceSnapshotAsync("seaweedfs", cancellationToken);
            Assert.Equal(storageBeforeApi.ResourceId, storageAfterRepeatedEnsure.ResourceId);
            Assert.Equal(storageBeforeApi.Snapshot.StartTimeStamp, storageAfterRepeatedEnsure.Snapshot.StartTimeStamp);

            // When 并发确保 API；Then Redis、SeaweedFS 与 API 健康，管理员只初始化一次。
            await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => fixture.EnsureApiStartedAsync(cancellationToken)));
            var apiBeforeRestart = await fixture.GetResourceSnapshotAsync("api", cancellationToken);
            var redis = await fixture.GetResourceSnapshotAsync("redis", cancellationToken);
            var storageBeforeRestart = await fixture.GetResourceSnapshotAsync("seaweedfs", cancellationToken);
            Assert.Equal(KnownResourceStates.Running, redis.Snapshot.State?.Text);
            Assert.Equal(KnownResourceStates.Running, apiBeforeRestart.Snapshot.State?.Text);
            Assert.Equal(storageBeforeApi.Snapshot.StartTimeStamp, storageBeforeRestart.Snapshot.StartTimeStamp);
            await AssertResourcesNotStartedAsync(fixture, ["web"], cancellationToken);
            await AssertResourcesNotStartedAsync(fixture, fixture.FrontendInstallerResourceNames, cancellationToken);
            Assert.Single(await fixture.GetApiLogLinesAsync("管理员初始化成功", cancellationToken));

            var postgresAfterApiStart = await fixture.GetResourceSnapshotAsync("postgres", cancellationToken);
            Assert.Equal(postgresBefore.ResourceId, postgresAfterApiStart.ResourceId);
            Assert.Equal(postgresBefore.Snapshot.StartTimeStamp, postgresAfterApiStart.Snapshot.StartTimeStamp);

            // When 显式重启 SeaweedFS；Then 它获得新的启动时间，API 保持原实例。
            await fixture.RestartStorageAsync(cancellationToken);
            var storageAfterRestart = await fixture.GetResourceSnapshotAsync("seaweedfs", cancellationToken);
            var apiAfterRestart = await fixture.GetResourceSnapshotAsync("api", cancellationToken);
            Assert.True(storageAfterRestart.Snapshot.StartTimeStamp > storageBeforeRestart.Snapshot.StartTimeStamp);
            Assert.Equal(apiBeforeRestart.ResourceId, apiAfterRestart.ResourceId);
            Assert.Equal(apiBeforeRestart.Snapshot.StartTimeStamp, apiAfterRestart.Snapshot.StartTimeStamp);

            // When 重复 Ensure；Then 缓存任务不触发额外重启或重复管理员初始化。
            await Task.WhenAll(
                fixture.EnsureStorageStartedAsync(cancellationToken),
                fixture.EnsureApiStartedAsync(cancellationToken));
            var storageAfterEnsure = await fixture.GetResourceSnapshotAsync("seaweedfs", cancellationToken);
            var apiAfterEnsure = await fixture.GetResourceSnapshotAsync("api", cancellationToken);
            Assert.Equal(storageAfterRestart.Snapshot.StartTimeStamp, storageAfterEnsure.Snapshot.StartTimeStamp);
            Assert.Equal(apiAfterRestart.Snapshot.StartTimeStamp, apiAfterEnsure.Snapshot.StartTimeStamp);
            Assert.Single(await fixture.GetApiLogLinesAsync("管理员初始化成功", cancellationToken));

            var secretsDirectory = fixture.TestSecretsDirectoryPath;
            Assert.NotNull(secretsDirectory);
            Assert.True(await fixture.TestStorageVolumeExistsAsync(cancellationToken));

            // When 释放 fixture；Then 唯一测试密钥目录与 SeaweedFS 专用卷都被删除。
            disposeAttempted = true;
            await fixture.DisposeAsync();
            Assert.False(Directory.Exists(secretsDirectory));
            Assert.False(await fixture.TestStorageVolumeExistsAsync(cancellationToken));
        }
        finally
        {
            if (!disposeAttempted)
            {
                disposeAttempted = true;
                await fixture.DisposeAsync();
            }
        }
    }

    private static async Task AssertResourcesNotStartedAsync(
        AspireIntegrationFixture fixture,
        IEnumerable<string> resourceNames,
        CancellationToken cancellationToken)
    {
        foreach (var resourceName in resourceNames)
        {
            var resource = await fixture.GetResourceSnapshotAsync(resourceName, cancellationToken);
            Assert.Equal(KnownResourceStates.NotStarted, resource.Snapshot.State?.Text);
        }
    }
}
