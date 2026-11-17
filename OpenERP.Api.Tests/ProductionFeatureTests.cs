using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenERP.Api;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ProductionFeatureTests
{
    [Fact]
    public async Task Production_exposes_manual_and_scheduled_order_sync_endpoints()
    {
        // Given ERP 已配置，API 以 Production 环境运行。
        var ct = TestContext.Current.CancellationToken;
        var builder = CreateBuilder("Production");
        builder.Services.AddSingleton<IErpClient>(new TestErpClient());
        builder.Services.AddSingleton<IOrderSyncSettingsStore>(new TestSettingsStore());
        builder.Services.AddSingleton<OrderSyncJobs>();
        builder.Services.AddOptions<OrderSyncSchedulerOptions>();
        builder.Services.AddSingleton<OrderSyncScheduler>();
        await using var app = builder.Build();
        app.MapErp();
        await app.StartAsync(ct);
        using var http = CreateClient(app);

        // When 查询同步能力、读取设置并启动手动同步。
        using var configResponse = await http.GetAsync("/api/erp/config", ct);
        var config = await configResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var settingsResponse = await http.GetAsync("/api/erp/order-sync-settings", ct);
        using var manualSyncResponse = await http.PostAsync("/api/erp/shops/30/order-sync", null, ct);

        // Then Production 明确报告同步已启用，设置接口可用，手动同步请求被受理。
        Assert.Equal(HttpStatusCode.OK, configResponse.StatusCode);
        Assert.True(config.GetProperty("orderSyncEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, manualSyncResponse.StatusCode);
    }

    [Fact]
    public async Task Production_health_and_liveness_are_anonymous_and_return_no_check_details()
    {
        // Given 默认健康检查已注册，API 以 Production 环境运行。
        var ct = TestContext.Current.CancellationToken;
        var builder = CreateBuilder("Production");
        builder.AddServiceDefaults();
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync(ct);
        using var http = CreateClient(app);

        // When 匿名请求健康与存活端点。
        using var health = await http.GetAsync("/health", ct);
        using var alive = await http.GetAsync("/alive", ct);
        var healthBody = await health.Content.ReadAsStringAsync(ct);
        var aliveBody = await alive.Content.ReadAsStringAsync(ct);

        // Then 两个端点均可用，响应仅包含总体状态，不泄露检查项详情。
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        Assert.Equal("Healthy", healthBody);
        Assert.Equal("Healthy", aliveBody);
        Assert.DoesNotContain("self", healthBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("self", aliveBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Production_keeps_api_authentication_and_csrf_checks_active()
    {
        // Given Production 沿用管理员认证、授权与 antiforgery 管道。
        var ct = TestContext.Current.CancellationToken;
        var builder = CreateBuilder("Production");
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            "Host=127.0.0.1;Database=openerp_unused;Username=openerp_unused;Password=unused"));
        builder.Services.AddAccountAuthentication();
        builder.Services.RemoveAll<IDataProtectionProvider>();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "127.0.0.1",
            ["Deployment:KnownProxies:0"] = "127.0.0.1"
        });
        builder.ConfigureProductionHosting();
        await using var app = builder.Build();
        app.UseProductionHosting();
        app.UseAccountAuthentication();
        app.MapAccounts();
        app.MapGet("/api/private", () => Results.Ok(new { value = "protected" }));
        await app.StartAsync(ct);
        using var http = CreateClient(app);
        http.DefaultRequestHeaders.Host = "127.0.0.1";
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        // When 匿名读取管理员资料，并向匿名登录路由发送无 CSRF token 的写请求。
        using var profile = await http.GetAsync("/api/auth/me", ct);
        using var login = await http.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "initial-password" }, ct);
        using var privateApi = await http.GetAsync("/api/private", ct);
        var loginBody = await login.Content.ReadAsStringAsync(ct);

        // Then API 仍受保护，登录写请求仍须通过 CSRF 校验。
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        Assert.True(login.StatusCode == HttpStatusCode.BadRequest, $"预期 CSRF 拒绝为 400，实际为 {(int)login.StatusCode}，响应：{loginBody}");
        Assert.Equal(HttpStatusCode.Unauthorized, privateApi.StatusCode);
    }

    private static WebApplicationBuilder CreateBuilder(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        return builder;
    }

    private static HttpClient CreateClient(WebApplication app) =>
        new(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
}
