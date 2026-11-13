using System.Diagnostics;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.JavaScript;
using Aspire.Hosting.Publishing;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class FileStorageFixture : IAsyncLifetime
{
    private const string StorageName = "seaweedfs";
    private readonly string _volumeName = $"openerp-test-seaweedfs-{Guid.NewGuid():N}";
    private DirectoryInfo? _testSecretsDirectory;
    private string? _containerRuntime;
    private IDistributedApplicationTestingBuilder? _appHost;
    private DistributedApplication? _app;

    internal Uri ApiEndpoint => _app!.GetEndpoint("api", "https");

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
        foreach (var frontend in appHost.Resources.Where(resource => resource is JavaScriptAppResource or JavaScriptInstallerResource))
        {
            appHost.CreateResourceBuilder(frontend).WithExplicitStart();
        }

        _app = await appHost.BuildAsync(timeout.Token);
#pragma warning disable ASPIRECONTAINERRUNTIME001 // 使用 Aspire 的运行时选择规则定位测试卷。
        var runtime = await _app.Services.GetRequiredService<IContainerRuntimeResolver>().ResolveAsync(timeout.Token);
        _containerRuntime = runtime.Name.ToLowerInvariant();
#pragma warning restore ASPIRECONTAINERRUNTIME001
        await _app.StartAsync(timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);
    }

    public HttpClient CreateApiClient()
    {
        var client = _app!.CreateHttpClient("api", "https");
        client.Timeout = TimeSpan.FromMinutes(3);
        return client;
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
            throw new AggregateException("文件存储集成测试清理失败。", errors);
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
