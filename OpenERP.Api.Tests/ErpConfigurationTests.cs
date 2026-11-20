using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ErpConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseConnectionFailureReturnsAReadableError(bool readToken)
    {
        // Given 本机端口已绑定但未监听，没有数据库能接受连接。
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"Host=127.0.0.1;Port={port};Username=test;Database=test;Pooling=false;Timeout=1")
            .Options;
        await using var db = new AppDbContext(options);
        var store = new ErpTokenStore(db, new EphemeralDataProtectionProvider());
        var ct = TestContext.Current.CancellationToken;

        // When 查询配置或解析凭证。Then 调用方收到对应的业务错误，可以直接返回或记录。
        if (readToken)
        {
            var token = await store.GetAsync(ct);
            if (token.IsOk)
                throw new Xunit.Sdk.XunitException("数据库不可用时仍返回了凭证。");
            Assert.Equal("读取私有 ERP AccessToken 失败，请稍后重试。", token.Error.Message);
        }
        else
        {
            var config = await store.GetConfigurationAsync(ct);
            if (config.IsOk)
                throw new Xunit.Sdk.XunitException("数据库不可用时仍返回了配置。");
            Assert.Equal("读取私有 ERP 配置失败，请稍后重试。", config.Error.Message);
        }
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", true)]
    public async Task ConfigurationReportsOrderSyncCapability(string environment, bool expected)
    {
        // Given API 已配置 ERP Token，并以指定环境运行。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(environment, new TestErpTokenStore("test-secret"), ct);
        using var http = ErpEndpointTestAuth.CreateClient(app, UserAccount.AdministratorRole);

        // When 浏览器查询 ERP 配置。
        using var configResponse = await http.GetAsync("/api/erp/config", ct);
        if (!configResponse.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException(await configResponse.Content.ReadAsStringAsync(ct));
        var config = await configResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Then 开发和生产环境声明支持同步，ERP 配置状态仍可读取，凭证不出现在响应中。
        Assert.True(config.GetProperty("configured").GetBoolean());
        Assert.Equal("https://v3-api.wxwerp.com", config.GetProperty("baseUrl").GetString());
        Assert.DoesNotContain("test-secret", config.GetRawText());
        Assert.True(config.TryGetProperty("orderSyncEnabled", out var capability), "响应缺少订单同步能力字段。");
        Assert.Equal(expected, capability.GetBoolean());

        // When 旧客户端访问已迁移到 Hub 的手动同步接口。
        using var status = await http.GetAsync("/api/erp/shops/30/order-sync", ct);
        using var start = await http.PostAsync("/api/erp/shops/30/order-sync", null, ct);

        // Then 旧路由不再提供，设置 API 保留原有环境限制。
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
        using var settings = await http.GetAsync("/api/erp/order-sync-settings", ct);
        Assert.Equal(expected ? HttpStatusCode.OK : HttpStatusCode.NotFound, settings.StatusCode);
        if (expected)
            return;

        using var selection = await http.GetAsync("/api/erp/order-sync-settings/shops", ct);
        Assert.Equal(HttpStatusCode.NotFound, selection.StatusCode);
        using var save = await http.PutAsync("/api/erp/order-sync-settings", null, ct);
        Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/erp/config")]
    [InlineData("PUT", "/api/erp/config")]
    [InlineData("GET", "/api/erp/order-sync-settings")]
    [InlineData("PUT", "/api/erp/order-sync-settings")]
    [InlineData("GET", "/api/erp/order-sync-settings/shops")]
    public async Task Ordinary_user_cannot_read_or_write_erp_configuration(string method, string route)
    {
        // Given 已登录普通用户，ERP 已配置。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync("Production", new TestErpTokenStore("test-secret"), ct);
        using var http = ErpEndpointTestAuth.CreateClient(app, UserAccount.UserRole);

        // When 直接访问 ERP 或定时同步设置。
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (request.Method == HttpMethod.Put)
            request.Content = JsonContent.Create(new { accessToken = "changed", enabled = false, shopSids = Array.Empty<int>() });
        using var response = await http.SendAsync(request, ct);

        // Then 服务端拒绝普通用户。
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Production", true, true)]
    [InlineData("Production", false, true)]
    [InlineData("Staging", true, false)]
    public async Task Ordinary_user_can_read_only_order_sync_availability(
        string environment, bool configured, bool orderSyncEnabled)
    {
        // Given 普通用户已登录，环境和 ERP Token 状态已知。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(environment, new TestErpTokenStore(configured ? "test-secret" : ""), ct);
        using var http = ErpEndpointTestAuth.CreateClient(app, UserAccount.UserRole);

        // When 订单同步界面查询可用性。
        using var response = await http.GetAsync("/api/erp/order-sync-availability", ct);

        // Then 响应仅包含 ERP 配置状态与环境支持状态。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(configured, payload.GetProperty("configured").GetBoolean());
        Assert.Equal(orderSyncEnabled, payload.GetProperty("orderSyncEnabled").GetBoolean());
        Assert.Equal(2, payload.EnumerateObject().Count());
    }

    [Fact]
    public async Task Availability_reports_unavailable_configuration_to_authenticated_user()
    {
        // Given 普通用户已登录，ERP 配置存储暂时不可读。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync("Production", new FailingErpTokenStore(), ct);
        using var http = ErpEndpointTestAuth.CreateClient(app, UserAccount.UserRole);

        // When 查询可用性。Then 返回 503，客户端可以区分尚未配置与暂时不可用。
        using var response = await http.GetAsync("/api/erp/order-sync-availability", ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Availability_requires_authentication()
    {
        // Given 访客未登录。When 查询可用性。Then 拒绝匿名请求。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync("Production", new TestErpTokenStore("test-secret"), ct);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await http.GetAsync("/api/erp/order-sync-availability", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<WebApplication> StartAppAsync(
        string environment, IErpTokenStore tokens, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(tokens);
        builder.Services.AddSingleton<IErpClient, TestErpClient>();
        ErpEndpointTestAuth.Configure(builder);
        builder.Services.AddSingleton<OrderSyncJobs>();
        builder.Services.AddSingleton<IOrderSyncSettingsStore>(new TestSettingsStore());
        builder.Services.AddOptions<OrderSyncSchedulerOptions>();
        builder.Services.AddSingleton<OrderSyncScheduler>();
        var app = builder.Build();
        app.MapErp();
        try
        {
            await app.StartAsync(ct);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }
}
