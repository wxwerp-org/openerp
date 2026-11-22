using System.Diagnostics;
using System.Net.Security;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

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
        await using var fixture = new AspireIntegrationFixture(initializeAdmin: false, useBrowserErpStub: true);
        await fixture.InitializeAsync();
        var endpoint = await fixture.StartWebAsync(timeout.Token);
        Assert.NotEmpty(await fixture.GetApiLogLinesAsync(BrowserErpStartup.ReadyMessage, timeout.Token));
        // Given 两店已有历史订单，店铺目录等待管理员通过上游传输桩同步。
        await SeedOrderAsync(fixture, timeout.Token);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenERP.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        // When 浏览器通过真实页面初始化管理员、登录、编辑账户并回归工作台。
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
        start.Environment["OPENERP_TEST_LOCAL_ORDERS"] = "1";
        start.Environment["OPENERP_TEST_BROWSER_ERP_TOKEN"] = BrowserErpStartup.TestToken;
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

    private static async Task SeedOrderAsync(AspireIntegrationFixture fixture, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(await fixture.GetPostgresConnectionStringAsync(ct)).Options;
        await using var db = new AppDbContext(options);
        // 两张主单覆盖两个平台店铺，供商品量报表的平台分布和店铺排行验收。
        var order = new OrderSnapshot("browser-local-order", "erp-internal-order", 30, "TemuPop", "处理中",
            new(new("2026-11-17 08:30", "list"), null, null, null),
            [new("TM-72801-A", 30, "shipped", "已发货"), new("TM-72801-B", 30, "pending", "待发货")],
            [new(null, null, null, "SKU-1", 2, null, null), new(null, null, null, "SKU-2", 3, null, null)]);
        var secondShopOrder = new OrderSnapshot("browser-second-shop-order", "erp-second-order", 31, "Temu", "已完成",
            new(null, null, new("2026-11-18 10:00", "list"), null),
            [new("TM-90021", 31, "completed", "已完成")],
            [new(null, null, "SKU-B1", null, 3, false, false)]);
        var saved = await new OrderStore(db).SaveAsync([order, secondShopOrder], ct);
        if (saved.IsErr)
            throw new XunitException(saved.Error.Message);
        Assert.Equal(2, saved.Value.Added);
    }
}
