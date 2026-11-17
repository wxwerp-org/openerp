using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    private const string CiResultsDirectoryVariable = "OPENERP_CI_RESULTS_DIR";
    private static readonly Regex SensitiveValueRegex = new(
        "(?i)(?<key>\\b[A-Z0-9_.-]*(?:PASSWORD|SECRET(?:KEY)?|TOKEN|API[_-]?KEY|AUTHORIZATION|CONNECTIONSTRING)[A-Z0-9_.-]*\\b\\s*[:=]\\s*)(?:\"[^\"]*\"|'[^']*'|[^,\\s;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string _volumeName = $"openerp-test-seaweedfs-{Guid.NewGuid():N}";
    private readonly string _fixtureId = Guid.NewGuid().ToString("N");
    private readonly string _postgresPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly string _seaweedFsSecretKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly List<string> _diagnosticFailures = [];
    private DirectoryInfo? _testSecretsDirectory;
    private string? _containerRuntime;
    private IDistributedApplicationTestingBuilder? _appHost;
    private DistributedApplication? _app;
    private string _currentStage = "尚未初始化";
    private string? _failureStage;
    private string? _diagnosticFilePath;
    private string? _diagnosticSnapshot;

    internal string AdminPassword { get; } = $" {Convert.ToHexString(RandomNumberGenerator.GetBytes(8))} ";
    internal Uri ApiEndpoint => _app!.GetEndpoint("api", "https");

    public async ValueTask InitializeAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            SetStage("创建 Aspire 测试宿主");
            // Aspire 测试宿主按不区分大小写的配置加载启动配置，显式提供 NO_PROXY 以兼容两种拼写。
            var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
                [
                    "--NO_PROXY=localhost,127.0.0.1,::1",
                    "--Parameters:wxw-erp-access-token=",
                    $"--Parameters:postgres-password={_postgresPassword}",
                    $"--Parameters:seaweedfs-secretKey={_seaweedFsSecretKey}",
                    $"--ASPIRE_USER_SECRETS_ID={_volumeName}",
                    "--DcpPublisher:WaitForResourceCleanup=true"
                ],
                timeout.Token);
            _appHost = appHost;
            SetStage("验证测试 User Secrets 隔离");
            if (appHost.UserSecretsManager.FilePath is { } secretsPath)
            {
                if (new FileInfo(secretsPath).Directory is not { } directory || directory.Name != _volumeName)
                {
                    throw new InvalidOperationException("Aspire 未使用本次测试专用的密钥目录。");
                }

                _testSecretsDirectory = directory;
            }

            SetStage("配置测试资源隔离");
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

            // 测试已构建 API，由 Aspire 直接启动 DLL，避免 dotnet run 父进程在 Linux 停止超时。
#pragma warning disable ASPIREPROJECTS001 // 只覆盖测试宿主的启动参数，仍由 Aspire 管理端点、重启与清理。
            appHost.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "api")
                .Annotations.Add(new ProjectLaunchArgsOverrideAnnotation([typeof(OpenERP.Api.FileStore).Assembly.Location]));
#pragma warning restore ASPIREPROJECTS001

            SetStage("构建 Aspire 测试应用");
            _app = await appHost.BuildAsync(timeout.Token);
#pragma warning disable ASPIRECONTAINERRUNTIME001 // 使用 Aspire 的运行时选择规则定位测试卷。
            SetStage("解析容器运行时");
            var runtime = await _app.Services.GetRequiredService<IContainerRuntimeResolver>().ResolveAsync(timeout.Token);
            _containerRuntime = runtime.Name.ToLowerInvariant();
#pragma warning restore ASPIRECONTAINERRUNTIME001
            SetStage("启动 Aspire 资源");
            await _app.StartAsync(timeout.Token);
            SetStage("等待 api 健康");
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);
            if (_initializeAdmin)
            {
                SetStage("初始化测试管理员");
                using var client = CreateApiClient();
                await AuthenticationIntegrationTests.RefreshCsrfAsync(client, timeout.Token);
                using var response = await client.PostAsJsonAsync("/api/auth/initialize",
                    new { newPassword = AdminPassword }, timeout.Token);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
            SetStage("运行集成测试");
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(_currentStage, exception);
            throw;
        }
    }

    public HttpClient CreateApiClient()
    {
        return new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = ApiEndpoint,
            Timeout = TimeSpan.FromMinutes(3)
        };
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

    internal async Task<Uri> StartWebAsync(CancellationToken ct)
    {
        try
        {
            var resources = _appHost!.Resources;
            foreach (var installer in resources.OfType<JavaScriptInstallerResource>())
            {
                SetStage($"启动 {installer.Name}");
                var install = await _app!.ResourceCommands.ExecuteCommandAsync(installer.Name, KnownResourceCommands.StartCommand, ct);
                Assert.True(install.Success, install.Message);
            }

            var web = resources.OfType<IResourceWithEndpoints>().Single(resource => resource.Name == "web");
            SetStage($"启动 {web.Name}");
            var start = await _app!.ResourceCommands.ExecuteCommandAsync(web.Name, KnownResourceCommands.StartCommand, ct);
            Assert.True(start.Success, start.Message);
            SetStage($"等待 {web.Name} 健康");
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(web.Name, ct);
            SetStage($"读取 {web.Name} HTTPS 端点");
            var endpoints = web.GetEndpoints().Select(endpoint => new Uri(endpoint.Url)).ToArray();
            var endpoint = Assert.Single(endpoints, endpoint => endpoint.Scheme == Uri.UriSchemeHttps);
            SetStage("运行集成测试");
            return endpoint;
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(_currentStage, exception);
            throw;
        }
    }

    internal async Task RestartApiAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var notifications = _app!.ResourceNotifications;
            SetStage("等待 api 重启前健康");
            var before = await notifications.WaitForResourceHealthyAsync("api", timeout.Token);
            SetStage("重启 api");
            var restart = await _app.ResourceCommands.ExecuteCommandAsync("api", KnownResourceCommands.RestartCommand, timeout.Token);
            Assert.True(restart.Success, restart.Message);
            SetStage("等待 api 重启完成");
            await notifications.WaitForResourceAsync("api", resource =>
                resource.Snapshot.State?.Text == KnownResourceStates.Running &&
                resource.Snapshot.StartTimeStamp > before.Snapshot.StartTimeStamp, timeout.Token);
            SetStage("等待 api 重启后健康");
            await notifications.WaitForResourceHealthyAsync("api", timeout.Token);
            SetStage("运行集成测试");
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(_currentStage, exception);
            throw;
        }
    }

    public async Task RestartStorageAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var notifications = _app!.ResourceNotifications;
            await RestartStorageAsync(_app.ResourceCommands.ExecuteCommandAsync, notifications.WaitForResourceAsync,
                notifications.WaitForResourceHealthyAsync, timeout.Token, SetStage);
            SetStage("运行集成测试");
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(_currentStage, exception);
            throw;
        }
    }

    internal static async Task RestartStorageAsync(
        Func<string, string, CancellationToken, Task<ExecuteCommandResult>> executeCommand,
        Func<string, Func<ResourceEvent, bool>, CancellationToken, Task<ResourceEvent>> waitForResource,
        Func<string, CancellationToken, Task<ResourceEvent>> waitForHealthy,
        CancellationToken cancellationToken,
        Action<string>? stageChanged = null)
    {
        stageChanged?.Invoke("等待 SeaweedFS 重启前健康");
        var beforeRestart = await waitForHealthy(StorageName, cancellationToken);
        stageChanged?.Invoke("重启 SeaweedFS");
        var restart = await executeCommand(StorageName, KnownResourceCommands.RestartCommand, cancellationToken);
        Assert.True(restart.Success, restart.Message);

        // 等待新一轮启动，避免沿用重启前的健康状态。
        stageChanged?.Invoke("等待 SeaweedFS 重启完成");
        await waitForResource(StorageName, resource =>
            resource.Snapshot.State?.Text == KnownResourceStates.Running &&
            resource.Snapshot.StartTimeStamp > beforeRestart.Snapshot.StartTimeStamp, cancellationToken);
        stageChanged?.Invoke("等待 SeaweedFS 重启后健康");
        await waitForHealthy(StorageName, cancellationToken);
        stageChanged?.Invoke("等待 api 健康");
        await waitForHealthy("api", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        var errors = new List<Exception>();
        await WriteDiagnosticsAsync("清理前");

        try
        {
            if (_appHost is not null)
            {
                SetStage("清理 Aspire 测试宿主");
                await _appHost.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception);
            await RecordFailureAsync("清理 Aspire 测试宿主", exception);
        }

        try
        {
            if (_containerRuntime is { } runtime)
            {
                SetStage("清理 SeaweedFS 测试卷");
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
            await RecordFailureAsync("清理 SeaweedFS 测试卷", exception);
        }

        try
        {
            if (_testSecretsDirectory is { Exists: true })
            {
                SetStage("清理测试 User Secrets");
                _testSecretsDirectory.Delete(recursive: true);
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception);
            await RecordFailureAsync("清理测试 User Secrets", exception);
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Aspire 集成测试清理失败。", errors);
        }
    }

    private void SetStage(string stage) => _currentStage = stage;

    private async Task RecordFailureAsync(string stage, Exception exception)
    {
        _failureStage ??= stage;
        _diagnosticFailures.Add(RedactDiagnosticText(exception.GetType().Name + ": " + exception.Message));

        if (_diagnosticSnapshot is null)
        {
            await WriteDiagnosticsAsync(stage);
            return;
        }

        _diagnosticSnapshot += $"{Environment.NewLine}失败阶段: {RedactDiagnosticText(stage)}{Environment.NewLine}失败: {_diagnosticFailures[^1]}{Environment.NewLine}";
        await PersistDiagnosticSnapshotAsync();
    }

    private async Task WriteDiagnosticsAsync(string stage)
    {
        var resultsDirectory = Environment.GetEnvironmentVariable(CiResultsDirectoryVariable);
        if (string.IsNullOrWhiteSpace(resultsDirectory))
        {
            return;
        }

        try
        {
            if (!Path.IsPathFullyQualified(resultsDirectory))
            {
                throw new InvalidOperationException($"{CiResultsDirectoryVariable} 必须是绝对路径。");
            }

            Directory.CreateDirectory(resultsDirectory);
            _diagnosticFilePath ??= Path.Combine(resultsDirectory, $"aspire-fixture-{_fixtureId}.log");
            _diagnosticSnapshot = await CreateDiagnosticSnapshotAsync(stage);
            await PersistDiagnosticSnapshotAsync();
        }
        catch (Exception diagnosticException)
        {
            ReportDiagnosticFailure(diagnosticException);
        }
    }

    private async Task<string> CreateDiagnosticSnapshotAsync(string stage)
    {
        var output = new StringBuilder();
        output.AppendLine($"Fixture: {_fixtureId}");
        output.AppendLine($"阶段: {RedactDiagnosticText(_failureStage ?? stage)}");
        output.AppendLine($"最近操作阶段: {RedactDiagnosticText(_currentStage)}");
        foreach (var failure in _diagnosticFailures)
        {
            output.AppendLine($"失败: {failure}");
        }

        output.AppendLine("资源状态:");
        if (_app is null || _appHost is null)
        {
            output.AppendLine("  Aspire 测试应用尚未构建。");
            output.AppendLine("控制台日志:");
            output.AppendLine("  Aspire 测试应用尚未构建。");
            return output.ToString();
        }

        var notifications = _app.ResourceNotifications;
        var resourceLogger = _app.Services.GetRequiredService<ResourceLoggerService>();
        foreach (var resource in _appHost.Resources)
        {
            if (notifications.TryGetCurrentState(resource.Name, out var state) && state is not null)
            {
                var currentState = state.Snapshot.State?.Text ?? "未知";
                output.AppendLine($"  {RedactDiagnosticText(resource.Name)}: {RedactDiagnosticText(currentState)}; 启动时间={state.Snapshot.StartTimeStamp:O}");
            }
            else
            {
                output.AppendLine($"  {RedactDiagnosticText(resource.Name)}: 状态不可用");
            }
        }

        output.AppendLine("控制台日志:");
        using var diagnosticsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var resource in _appHost.Resources)
        {
            output.AppendLine($"  [{RedactDiagnosticText(resource.Name)}]");
            var recentLines = new Queue<LogLine>();
            try
            {
                await using var logEnumerator = resourceLogger.GetAllAsync(resource).GetAsyncEnumerator(diagnosticsTimeout.Token);
                while (await logEnumerator.MoveNextAsync())
                {
                    foreach (var line in logEnumerator.Current)
                    {
                        if (recentLines.Count == 200)
                        {
                            recentLines.Dequeue();
                        }

                        recentLines.Enqueue(line);
                    }
                }

                if (recentLines.Count == 0)
                {
                    output.AppendLine("    (无日志)");
                }
                else
                {
                    foreach (var line in recentLines)
                    {
                        var stream = line.IsErrorMessage ? "stderr" : "stdout";
                        output.AppendLine($"    [{stream} #{line.LineNumber}] {RedactDiagnosticText(line.Content)}");
                    }
                }
            }
            catch (Exception logException)
            {
                output.AppendLine($"    读取日志失败: {RedactDiagnosticText(logException.GetType().Name + ": " + logException.Message)}");
            }
        }

        return output.ToString();
    }

    private async Task PersistDiagnosticSnapshotAsync()
    {
        if (_diagnosticFilePath is null || _diagnosticSnapshot is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(_diagnosticFilePath, _diagnosticSnapshot, CancellationToken.None);
        }
        catch (Exception diagnosticException)
        {
            ReportDiagnosticFailure(diagnosticException);
        }
    }

    private void ReportDiagnosticFailure(Exception exception)
    {
        var message = RedactDiagnosticText(exception.GetType().Name + ": " + exception.Message);
        try
        {
            Console.Error.WriteLine($"Aspire fixture {_fixtureId} 诊断保存失败: {message}");
        }
        catch
        {
            // 诊断输出失败不能覆盖原始阶段异常或清理异常。
        }
    }

    private string RedactDiagnosticText(string value)
    {
        foreach (var secret in new[] { AdminPassword, AdminPassword.Trim(), _postgresPassword, _seaweedFsSecretKey })
        {
            if (secret.Length > 0)
            {
                value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            }
        }

        return SensitiveValueRegex.Replace(value, match => $"{match.Groups["key"].Value}[REDACTED]");
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
