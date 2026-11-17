using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.JavaScript;
using Aspire.Hosting.Publishing;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OpenERP.Api.Tests;

[CollectionDefinition(nameof(AspireIntegrationCollection))]
public sealed class AspireIntegrationCollection : ICollectionFixture<AspireIntegrationFixture>;

public sealed class AspireIntegrationFixture : IAsyncLifetime
{
    public AspireIntegrationFixture() : this(initializeAdmin: true)
    {
    }

    internal AspireIntegrationFixture(bool initializeAdmin) => _initializeAdmin = initializeAdmin;

    private readonly bool _initializeAdmin;
    private const string StorageName = "seaweedfs";
    private readonly string _volumeName = $"openerp-test-seaweedfs-{Guid.NewGuid():N}";
    private readonly object _startupLock = new();
    private Task? _storageStartTask;
    private Task? _redisStartTask;
    private Task? _apiStartTask;
    private Task<Uri>? _webStartTask;
    private DirectoryInfo? _testSecretsDirectory;
    private string? _containerRuntime;
    private IDistributedApplicationTestingBuilder? _appHost;
    private DistributedApplication? _app;

    internal string AdminPassword { get; } = $" {Convert.ToHexString(RandomNumberGenerator.GetBytes(8))} ";
    internal Uri ApiEndpoint
    {
        get
        {
            lock (_startupLock)
            {
                if (_apiStartTask is not { IsCompletedSuccessfully: true })
                {
                    throw new InvalidOperationException("API 尚未就绪；请先等待 EnsureApiStartedAsync 完成。");
                }
            }

            return _app!.GetEndpoint("api", "https");
        }
    }

    internal IReadOnlyList<string> FrontendInstallerResourceNames =>
        _appHost?.Resources.OfType<JavaScriptInstallerResource>().Select(resource => resource.Name).ToArray() ?? [];

    internal string? TestSecretsDirectoryPath => _testSecretsDirectory?.FullName;

    public async ValueTask InitializeAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        // Aspire 测试宿主按不区分大小写的配置加载启动配置，显式提供 NO_PROXY 以兼容两种拼写。
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            [
                "--NO_PROXY=localhost,127.0.0.1,::1",
                "--Parameters:wxw-erp-access-token=",
                $"--Parameters:postgres-password={Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}",
                $"--Parameters:seaweedfs-secretKey={Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}",
                $"--ASPIRE_USER_SECRETS_ID={_volumeName}",
                "--DcpPublisher:WaitForResourceCleanup=true"
            ],
            timeout.Token);
        _appHost = appHost;
        if (appHost.UserSecretsManager.FilePath is { } secretsPath)
        {
            if (new FileInfo(secretsPath).Directory is not { } directory || directory.Name != _volumeName)
            {
                throw new InvalidOperationException("Aspire 未使用本次测试专用的密钥目录。");
            }

            _testSecretsDirectory = directory;
        }

        // 所有容器均属于当前测试会话，SeaweedFS 的专用卷可跨容器重建保留数据。
        foreach (var container in appHost.Resources.OfType<ContainerResource>())
        {
            foreach (var volume in container.Annotations.OfType<ContainerMountAnnotation>()
                .Where(mount => mount.Type == ContainerMountType.Volume).ToArray())
            {
                container.Annotations.Remove(volume);
            }

            appHost.CreateResourceBuilder(container).WithLifetime(ContainerLifetime.Session);
        }

        appHost.CreateResourceBuilder(appHost.Resources.OfType<SeaweedFSContainerResource>().Single())
            .WithDataVolume(name: _volumeName);
        foreach (var resource in appHost.Resources.Where(resource =>
            resource.Name is "api" or "redis" or StorageName ||
            resource is JavaScriptAppResource or JavaScriptInstallerResource))
        {
            appHost.CreateResourceBuilder(resource).WithExplicitStart();
        }

        // 测试已构建 API，由 Aspire 直接启动 DLL，避免 dotnet run 父进程在 Linux 停止超时。
#pragma warning disable ASPIREPROJECTS001 // 仍由 Aspire 管理端点、重启与清理。
        appHost.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "api")
            .Annotations.Add(new ProjectLaunchArgsOverrideAnnotation([typeof(OpenERP.Api.FileStore).Assembly.Location]));
#pragma warning restore ASPIREPROJECTS001

        _app = await appHost.BuildAsync(timeout.Token);
#pragma warning disable ASPIRECONTAINERRUNTIME001 // 使用 Aspire 的运行时选择规则定位测试卷。
        var runtime = await _app.Services.GetRequiredService<IContainerRuntimeResolver>().ResolveAsync(timeout.Token);
        _containerRuntime = runtime.Name.ToLowerInvariant();
#pragma warning restore ASPIRECONTAINERRUNTIME001
        await _app.StartAsync(timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("openerp", timeout.Token);
    }

    public HttpClient CreateApiClient() => CreateApiClientForStartup(ApiEndpoint);

    internal Task EnsureStorageStartedAsync(CancellationToken cancellationToken)
    {
        lock (_startupLock)
        {
            return _storageStartTask ??= StartResourceAndWaitHealthyAsync(StorageName, cancellationToken);
        }
    }

    internal Task EnsureApiStartedAsync(CancellationToken cancellationToken)
    {
        lock (_startupLock)
        {
            return _apiStartTask ??= StartApiAsync(cancellationToken);
        }
    }

    private Task EnsureRedisStartedAsync(CancellationToken cancellationToken)
    {
        lock (_startupLock)
        {
            return _redisStartTask ??= StartResourceAndWaitHealthyAsync("redis", cancellationToken);
        }
    }

    private async Task StartApiAsync(CancellationToken cancellationToken)
    {
        using var timeout = CreateStartupTimeout(cancellationToken);
        var startupToken = timeout.Token;
        await Task.WhenAll(
            EnsureStorageStartedAsync(startupToken),
            EnsureRedisStartedAsync(startupToken));

        await _app!.ResourceNotifications.WaitForResourceHealthyAsync("openerp", startupToken);
        var start = await _app.ResourceCommands.ExecuteCommandAsync("api", KnownResourceCommands.StartCommand, startupToken);
        Assert.True(start.Success, start.Message);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", startupToken);

        if (_initializeAdmin)
        {
            using var client = CreateApiClientForStartup(_app.GetEndpoint("api", "https"));
            await AuthenticationIntegrationTests.RefreshCsrfAsync(client, startupToken);
            using var response = await client.PostAsJsonAsync("/api/auth/initialize",
                new { newPassword = AdminPassword }, startupToken);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    private async Task StartResourceAndWaitHealthyAsync(string resourceName, CancellationToken cancellationToken)
    {
        using var timeout = CreateStartupTimeout(cancellationToken);
        var startupToken = timeout.Token;
        var start = await _app!.ResourceCommands.ExecuteCommandAsync(
            resourceName, KnownResourceCommands.StartCommand, startupToken);
        Assert.True(start.Success, start.Message);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, startupToken);
    }

    private static CancellationTokenSource CreateStartupTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        return timeout;
    }

    private static HttpClient CreateApiClientForStartup(Uri endpoint) =>
        new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = endpoint,
            Timeout = TimeSpan.FromMinutes(3)
        };

    internal async Task<ResourceEvent> GetResourceSnapshotAsync(string resourceName, CancellationToken cancellationToken)
    {
        var notifications = _app?.ResourceNotifications
            ?? throw new InvalidOperationException("Aspire 测试宿主尚未初始化。");
        if (notifications.TryGetCurrentState(resourceName, out var resource))
        {
            return resource;
        }

        return await notifications.WaitForResourceAsync(resourceName, static _ => true, cancellationToken);
    }

    internal async Task<bool> TestStorageVolumeExistsAsync(CancellationToken cancellationToken)
    {
        if (_containerRuntime is not { } runtime)
        {
            return false;
        }

        var volumes = await RunVolumeCommandAsync(runtime, cancellationToken, "ls", "--format", "{{.Name}}");
        return volumes.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(_volumeName, StringComparer.Ordinal);
    }

    internal async Task<IReadOnlyList<string>> GetApiLogLinesAsync(string marker, CancellationToken ct)
    {
        var logger = _app!.Services.GetRequiredService<ResourceLoggerService>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var api = await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);
        await foreach (var batch in logger.WatchAsync(api.ResourceId).WithCancellation(timeout.Token))
        {
            if (batch.Any(line => line.Content.Contains(marker, StringComparison.Ordinal)))
                break;
        }

        var matches = new List<string>();
        await foreach (var batch in logger.GetAllAsync(api.ResourceId).WithCancellation(timeout.Token))
        {
            matches.AddRange(batch.Select(line => line.Content).Where(line => line.Contains(marker, StringComparison.Ordinal)));
        }

        return matches;
    }

    internal async Task<string> GetPostgresConnectionStringAsync(CancellationToken cancellationToken) =>
        await _app!.GetConnectionStringAsync("openerp", cancellationToken)
            ?? throw new InvalidOperationException("Aspire 未提供订单测试的数据库连接。");

    internal async Task<string> GetStorageConnectionStringAsync(CancellationToken cancellationToken)
    {
        await EnsureStorageStartedAsync(cancellationToken);
        return await _app!.GetConnectionStringAsync(StorageName, cancellationToken)
            ?? throw new InvalidOperationException("Aspire 未提供文件存储连接。");
    }

    internal Task<Uri> StartWebAsync(CancellationToken ct)
    {
        lock (_startupLock)
        {
            return _webStartTask ??= StartWebCoreAsync(ct);
        }
    }

    private async Task<Uri> StartWebCoreAsync(CancellationToken cancellationToken)
    {
        using var timeout = CreateStartupTimeout(cancellationToken);
        var ct = timeout.Token;
        await EnsureApiStartedAsync(ct);
        var resources = _appHost!.Resources;
        foreach (var installer in resources.OfType<JavaScriptInstallerResource>())
        {
            var install = await _app!.ResourceCommands.ExecuteCommandAsync(installer.Name, KnownResourceCommands.StartCommand, ct);
            Assert.True(install.Success, install.Message);
        }

        var web = resources.OfType<IResourceWithEndpoints>().Single(resource => resource.Name == "web");
        var start = await _app!.ResourceCommands.ExecuteCommandAsync(web.Name, KnownResourceCommands.StartCommand, ct);
        Assert.True(start.Success, start.Message);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(web.Name, ct);
        var endpoints = web.GetEndpoints().Select(endpoint => new Uri(endpoint.Url)).ToArray();
        return Assert.Single(endpoints, endpoint => endpoint.Scheme == Uri.UriSchemeHttps);
    }

    internal async Task RestartApiAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var notifications = _app!.ResourceNotifications;
        var before = await notifications.WaitForResourceHealthyAsync("api", timeout.Token);
        var restart = await _app.ResourceCommands.ExecuteCommandAsync("api", KnownResourceCommands.RestartCommand, timeout.Token);
        Assert.True(restart.Success, restart.Message);
        await notifications.WaitForResourceAsync("api", resource =>
            resource.Snapshot.State?.Text == KnownResourceStates.Running &&
            resource.Snapshot.StartTimeStamp > before.Snapshot.StartTimeStamp, timeout.Token);
        await notifications.WaitForResourceHealthyAsync("api", timeout.Token);
    }

    public async Task RestartStorageAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var notifications = _app!.ResourceNotifications;
        await RestartStorageAsync(_app.ResourceCommands.ExecuteCommandAsync, notifications.WaitForResourceAsync,
            notifications.WaitForResourceHealthyAsync, timeout.Token);
    }

    internal static async Task RestartStorageAsync(
        Func<string, string, CancellationToken, Task<ExecuteCommandResult>> executeCommand,
        Func<string, Func<ResourceEvent, bool>, CancellationToken, Task<ResourceEvent>> waitForResource,
        Func<string, CancellationToken, Task<ResourceEvent>> waitForHealthy,
        CancellationToken cancellationToken)
    {
        var beforeRestart = await waitForHealthy(StorageName, cancellationToken);
        var restart = await executeCommand(StorageName, KnownResourceCommands.RestartCommand, cancellationToken);
        Assert.True(restart.Success, restart.Message);

        // 等待新一轮启动，避免沿用重启前的健康状态。
        await waitForResource(StorageName, resource =>
            resource.Snapshot.State?.Text == KnownResourceStates.Running &&
            resource.Snapshot.StartTimeStamp > beforeRestart.Snapshot.StartTimeStamp, cancellationToken);
        await waitForHealthy(StorageName, cancellationToken);
        await waitForHealthy("api", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        var errors = new List<Exception>();
        try
        {
            if (_appHost is not null)
            {
                await _appHost.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        try
        {
            if (_containerRuntime is { } runtime)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var volumes = await RunVolumeCommandAsync(runtime, timeout.Token, "ls", "--format", "{{.Name}}");
                if (volumes.Split('\n', StringSplitOptions.TrimEntries).Contains(_volumeName, StringComparer.Ordinal))
                {
                    // 运行时会拒绝删除仍被容器引用的卷。
                    await RunVolumeCommandAsync(runtime, timeout.Token, "rm", _volumeName);
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        try
        {
            if (_testSecretsDirectory is { Exists: true })
            {
                _testSecretsDirectory.Delete(recursive: true);
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Aspire 集成测试清理失败。", errors);
        }
    }

    private static async Task<string> RunVolumeCommandAsync(string runtime, CancellationToken cancellationToken, params string[] args)
    {
        var startInfo = new ProcessStartInfo(runtime)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "volume" }
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"无法启动 {runtime}。");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var standardError = await error;
        var standardOutput = await output;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{runtime} volume {string.Join(' ', args)} 失败，退出码 {process.ExitCode}。{standardError}");
        }

        return standardOutput;
    }
}
