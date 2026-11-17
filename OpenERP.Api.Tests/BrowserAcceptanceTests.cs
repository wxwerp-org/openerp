using System.Diagnostics;
using System.Net.Security;
using System.Text.Json;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class BrowserAcceptanceTests(ITestOutputHelper output)
{
    public static bool Enabled => Environment.GetEnvironmentVariable("OPENERP_RUN_BROWSER_TESTS") == "1";

    [Fact(Skip = "设置 OPENERP_RUN_BROWSER_TESTS=1 以运行隔离宿主上的真实浏览器验收。", SkipUnless = nameof(Enabled))]
    public async Task BrowserAccountAndWorkspaceFlowsUseRealAuthentication()
    {
        // Given 使用本次测试独立的未初始化数据库和 HTTPS 前端。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(8));
        await using var fixture = new AspireIntegrationFixture(initializeAdmin: false);
        await fixture.InitializeAsync();
        var endpoint = await fixture.StartWebAsync(timeout.Token);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenERP.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var workingDirectory = root.FullName;

        // When 浏览器通过真实页面初始化管理员、登录、编辑账户并回归工作台。
        // Node 在 macOS 上未必识别开发证书的本机信任；只导出已通过 .NET 严格校验的公钥证书。
        var certificatePath = Path.Combine(Path.GetTempPath(), $"openerp-browser-ca-{Guid.NewGuid():N}.pem");
        string? certificate = null;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, serverCertificate, _, errors) =>
            {
                if (errors != SslPolicyErrors.None || serverCertificate is null)
                    return false;
                certificate = serverCertificate.ExportCertificatePem();
                return true;
            }
        };
        using var http = new HttpClient(handler);
        using var probe = await http.GetAsync(new Uri(endpoint, "/api/auth/csrf"), timeout.Token);
        probe.EnsureSuccessStatusCode();
        Assert.NotNull(certificate);
        if (Environment.GetEnvironmentVariable("NODE_EXTRA_CA_CERTS") is { Length: > 0 } existingCa)
            certificate += "\n" + await File.ReadAllTextAsync(existingCa, timeout.Token);
        await File.WriteAllTextAsync(certificatePath, certificate, timeout.Token);

        var resultsDirectory = Environment.GetEnvironmentVariable("OPENERP_CI_RESULTS_DIR");
        var temporaryReport = string.IsNullOrWhiteSpace(resultsDirectory);
        var reportPath = temporaryReport
            ? Path.Combine(Path.GetTempPath(), $"openerp-browser-{Guid.NewGuid():N}.json")
            : Path.Combine(resultsDirectory!, "browser.json");

        async Task RunBrowserProcessAsync()
        {
            var start = new ProcessStartInfo("pnpm")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "--dir", "OpenERP.Web", "test:e2e", "--reporter=line,json",
                "--forbid-only", "--fail-on-flaky-tests", "--workers=1" })
                start.ArgumentList.Add(argument);
            start.Environment["PLAYWRIGHT_BASE_URL"] = endpoint.ToString();
            start.Environment["OPENERP_TEST_ADMIN_PASSWORD"] = fixture.AdminPassword;
            start.Environment["NODE_EXTRA_CA_CERTS"] = certificatePath;
            start.Environment["PLAYWRIGHT_JSON_OUTPUT_FILE"] = reportPath;

            using var process = Process.Start(start);
            Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!process.HasExited)
            {
                // 只清理本测试创建的浏览器测试进程，Aspire 资源仍由夹具管理。
                process.Kill(entireProcessTree: true);
                throw;
            }

            output.WriteLine(await stdout);
            output.WriteLine(await stderr);
            Assert.Equal(0, process.ExitCode);
        }

        try
        {
            await RunBrowserProcessAsync();
            // Playwright 没有 fail-skips 开关；外层成功必须包含实际浏览器执行且没有跳过。
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, timeout.Token));
            var statistics = report.RootElement.GetProperty("stats");
            Assert.True(statistics.GetProperty("expected").GetInt32() > 0, "未执行任何成功的浏览器用例。");
            Assert.Equal(0, statistics.GetProperty("skipped").GetInt32());
        }
        finally
        {
            File.Delete(certificatePath);
            if (temporaryReport)
                File.Delete(reportPath);
        }
    }
}
