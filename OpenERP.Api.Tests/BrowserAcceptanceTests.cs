using System.Diagnostics;
using System.Net.Security;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class BrowserAcceptanceTests(AspireIntegrationFixture fixture, ITestOutputHelper output)
    : IClassFixture<AspireIntegrationFixture>
{
    public static bool Enabled => Environment.GetEnvironmentVariable("OPENERP_RUN_BROWSER_TESTS") == "1";

    [Fact(Skip = "设置 OPENERP_RUN_BROWSER_TESTS=1 以运行隔离宿主上的真实浏览器验收。", SkipUnless = nameof(Enabled))]
    public async Task BrowserAccountAndWorkspaceFlowsUseRealAuthentication()
    {
        // Given 使用本次测试独立的数据库、管理员和 HTTPS 前端。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(8));
        var endpoint = await fixture.StartWebAsync(timeout.Token);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenERP.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        // When 浏览器通过真实页面执行登录、账户编辑和现有工作台回归。
        var start = new ProcessStartInfo("pnpm")
        {
            WorkingDirectory = root.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "--dir", "OpenERP.Web", "test:e2e", "--workers=1" })
            start.ArgumentList.Add(argument);
        start.Environment["PLAYWRIGHT_BASE_URL"] = endpoint.ToString();
        start.Environment["OPENERP_TEST_ADMIN_PASSWORD"] = fixture.AdminPassword;
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
        start.Environment["NODE_EXTRA_CA_CERTS"] = certificatePath;
        try
        {
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

            // Then 所有浏览器断言通过；详细场景由 Playwright 用例负责验证。
            output.WriteLine(await stdout);
            output.WriteLine(await stderr);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }
}
