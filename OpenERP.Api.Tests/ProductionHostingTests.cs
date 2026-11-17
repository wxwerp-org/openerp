using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ProductionHostingTests
{
    [Fact]
    public async Task Production_serves_built_assets_and_client_routes_without_capturing_api_or_health_paths()
    {
        // Given wwwroot 只包含静态构建产物，Production 同时映射 API 与健康端点。
        var ct = TestContext.Current.CancellationToken;
        var webRoot = Directory.CreateTempSubdirectory("openerp-production-web-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot.FullName, "index.html"), "<!doctype html><title>OpenERP</title>", ct);
            Directory.CreateDirectory(Path.Combine(webRoot.FullName, "assets"));
            await File.WriteAllTextAsync(Path.Combine(webRoot.FullName, "assets", "app.js"), "window.openErpBuilt = true;", ct);

            var builder = CreateBuilder("Production", webRoot.FullName, new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*"
            });
            builder.AddServiceDefaults();
            builder.ConfigureProductionHosting();
            await using var app = builder.Build();
            app.UseProductionHosting();
            app.MapGet("/api/ping", () => Results.Text("pong"));
            app.MapDefaultEndpoints();
            await app.StartAsync(ct);
            using var http = CreateClient(app);

            // When 读取构建文件、前端直达路由和未匹配的 API/健康路径。
            using var asset = await http.GetAsync("/assets/app.js", ct);
            using var clientRoute = await http.GetAsync("/orders/30", ct);
            using var api = await http.GetAsync("/api/ping", ct);
            using var missingApi = await http.GetAsync("/api/not-a-route", ct);
            using var missingHealth = await http.GetAsync("/health/private-detail", ct);
            var index = await clientRoute.Content.ReadAsStringAsync(ct);
            var missingApiBody = await missingApi.Content.ReadAsStringAsync(ct);

            // Then 静态资源与 SPA 路由可匿名访问，实际 API 正常路由，SPA fallback 不覆盖 API/健康命名空间。
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.Contains("window.openErpBuilt", await asset.Content.ReadAsStringAsync(ct));
            Assert.Equal(HttpStatusCode.OK, clientRoute.StatusCode);
            Assert.Contains("OpenERP", index);
            Assert.Equal("pong", await api.Content.ReadAsStringAsync(ct));
            Assert.Equal(HttpStatusCode.NotFound, missingApi.StatusCode);
            Assert.DoesNotContain("OpenERP", missingApiBody);
            Assert.Equal(HttpStatusCode.NotFound, missingHealth.StatusCode);
        }
        finally
        {
            webRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Production_applies_forwarded_protocol_only_for_the_configured_proxy_and_filters_hosts()
    {
        // Given 生产代理与 Host 均有明确配置。
        var ct = TestContext.Current.CancellationToken;
        var builder = CreateBuilder("Production", null, new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "erp.example.test",
            ["Deployment:KnownProxies:0"] = "127.0.0.1"
        });
        builder.ConfigureProductionHosting();
        await using var app = builder.Build();
        app.UseProductionHosting();
        app.MapGet("/test/scheme", (HttpContext context) => context.Request.Scheme);
        await app.StartAsync(ct);
        using var http = CreateClient(app);

        // When 配置中的受信任反向代理转发 HTTPS，随后使用未允许的 Host 访问。
        using var forwarded = await SendWithHeadersAsync(http, "erp.example.test", "https", ct);
        using var rejectedHost = await SendWithHeadersAsync(http, "attacker.example.test", "https", ct);

        // Then 受信任代理的协议被采纳，Host 白名单拒绝其他主机名。
        Assert.Equal(HttpStatusCode.OK, forwarded.StatusCode);
        Assert.Equal("https", await forwarded.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedHost.StatusCode);
    }

    [Fact]
    public async Task Production_ignores_forwarded_headers_from_an_unconfigured_proxy()
    {
        // Given Production 未配置任何可信代理。
        var ct = TestContext.Current.CancellationToken;
        var builder = CreateBuilder("Production", null, new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "erp.example.test"
        });
        builder.ConfigureProductionHosting();
        await using var app = builder.Build();
        app.UseProductionHosting();
        app.MapGet("/test/scheme", (HttpContext context) => context.Request.Scheme);
        await app.StartAsync(ct);
        using var http = CreateClient(app);

        // When 外部客户端伪造 X-Forwarded-Proto。
        using var response = await SendWithHeadersAsync(http, "erp.example.test", "https", ct);

        // Then 请求仍保留实际 HTTP 协议，不信任未配置的转发头。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http", await response.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData("", "127.0.0.1", "AllowedHosts")]
    [InlineData("*", "127.0.0.1", "AllowedHosts")]
    [InlineData("erp.example.test", "", "Deployment:KnownProxies")]
    [InlineData("erp.example.test", "not-an-ip", "Deployment:KnownProxies")]
    public void Production_container_requires_explicit_valid_host_and_proxy_configuration(
        string allowedHosts, string knownProxy, string expectedSetting)
    {
        // Given 容器 Production 模式缺少安全的 Host 或可信代理配置。
        var builder = CreateBuilder("Production", null, new Dictionary<string, string?>
        {
            ["OPENERP_RUNTIME_CONFIG"] = "/run/openerp/appsettings.json",
            ["AllowedHosts"] = allowedHosts,
            ["Deployment:KnownProxies:0"] = knownProxy
        });

        // When 注册生产 HTTP 配置。
        var exception = Assert.Throws<InvalidOperationException>(() => builder.ConfigureProductionHosting());

        // Then 启动前指出需要修复的配置项，而不允许开放 Host 或宽信任代理。
        Assert.Contains(expectedSetting, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_does_not_require_container_proxy_configuration()
    {
        // Given 本地 Development 环境使用默认配置。
        var builder = CreateBuilder("Development", null, new Dictionary<string, string?>());

        // When 注册生产 HTTP 配置。
        var exception = Record.Exception(() => builder.ConfigureProductionHosting());

        // Then 本地开发不被容器代理的强制配置阻塞。
        Assert.Null(exception);
    }

    private static WebApplicationBuilder CreateBuilder(
        string environment, string? webRootPath, IDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            WebRootPath = webRootPath
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static HttpClient CreateClient(WebApplication app) =>
        new(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };

    private static async Task<HttpResponseMessage> SendWithHeadersAsync(
        HttpClient client, string host, string forwardedProtocol, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/scheme");
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", forwardedProtocol);
        return await client.SendAsync(request, cancellationToken);
    }
}
