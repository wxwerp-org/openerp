using System.Diagnostics;
using System.Text.Json;
using Aspire.Hosting.JavaScript;
using Aspire.Hosting.Testing;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class WebInstallerTests
{
    [Fact]
    public async Task Installer_recreates_incompatible_modules_without_a_terminal_and_preserves_frozen_lockfile()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OpenERP_AppHost>(
            ["NO_PROXY=localhost,127.0.0.1,::1"], ct);
        var installer = Assert.Single(builder.Resources.OfType<JavaScriptInstallerResource>());
        var web = Assert.Single(builder.Resources.OfType<JavaScriptAppResource>());
        // Aspire 按最后一个 annotation 覆盖 AddViteApp 的默认 npm 配置。
        var command = web.Annotations.OfType<JavaScriptPackageManagerAnnotation>().Last().ExecutableName;
        var arguments = web.Annotations.OfType<JavaScriptInstallCommandAnnotation>().Last().Args;
        using var webPackage = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(installer.WorkingDirectory, "package.json"), ct));
        var packageManager = webPackage.RootElement.GetProperty("packageManager").GetString();
        var directory = Directory.CreateTempSubdirectory("openerp-web-installer-").FullName;
        try
        {
            // Given 本地依赖已安装到另一个 store，且安装进程没有 TTY、没有 CI 环境变量。
            Directory.CreateDirectory(Path.Combine(directory, "dependency"));
            await File.WriteAllTextAsync(Path.Combine(directory, "dependency", "package.json"),
                """{"name":"fixture-dependency","version":"1.0.0","main":"index.js"}""", ct);
            await File.WriteAllTextAsync(Path.Combine(directory, "dependency", "index.js"), "module.exports = 39", ct);
            var manifest = new
            {
                name = "installer-fixture",
                packageManager,
                dependencies = new Dictionary<string, string> { ["fixture-dependency"] = "file:./dependency" }
            };
            var packagePath = Path.Combine(directory, "package.json");
            await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(manifest), ct);
            var initial = await RunAsync(command, directory,
                ["install", "--store-dir", Path.Combine(directory, "old-store")], ct);
            Assert.True(initial.ExitCode == 0, initial.Output);
            var lockPath = Path.Combine(directory, "pnpm-lock.yaml");
            var originalLock = await File.ReadAllTextAsync(lockPath, ct);

            // When 使用实际 AppHost 的安装命令，Then 自动重建依赖且模块可用，锁文件保持原样。
            var installed = await RunAsync(command, directory, arguments, ct);
            Assert.True(installed.ExitCode == 0, installed.Output);
            var loaded = await RunAsync("node", directory,
                ["-e", "if (require('fixture-dependency') !== 39) process.exit(1)"], ct);
            Assert.True(loaded.ExitCode == 0, loaded.Output);
            Assert.Equal(originalLock, await File.ReadAllTextAsync(lockPath, ct));

            // Given package.json 与锁文件不一致，When 再次安装，Then 仍拒绝安装而不是改写锁文件。
            manifest.dependencies["another-dependency"] = "file:./dependency";
            await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(manifest), ct);
            var mismatched = await RunAsync(command, directory, arguments, ct);
            Assert.NotEqual(0, mismatched.ExitCode);
            Assert.Contains("ERR_PNPM_OUTDATED_LOCKFILE", mismatched.Output);
            Assert.Equal(originalLock, await File.ReadAllTextAsync(lockPath, ct));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string command, string directory, IEnumerable<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(command)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment.Remove("CI");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
