using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class ErpConfigurationTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public async Task ConfigurationReportsOrderSyncCapability(string environment, bool expected)
    {
        // Given API 已配置 ERP Token，并以指定环境运行。
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp:AccessToken"] = "test-secret"
        });
        builder.Services.AddHttpClient<IErpClient, WxwErpClient>();
        builder.Services.AddSingleton<OrderSyncJobs>();
        await using var app = builder.Build();
        app.MapErp();
        await app.StartAsync(ct);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        // When 浏览器查询 ERP 配置。
        var config = await http.GetFromJsonAsync<JsonElement>("/api/erp/config", ct);

        // Then 只有开发环境声明支持同步，ERP 配置状态仍可读取，凭证不出现在响应中。
        Assert.True(config.GetProperty("configured").GetBoolean());
        Assert.Equal("https://v3-api.wxwerp.com", config.GetProperty("baseUrl").GetString());
        Assert.DoesNotContain("test-secret", config.GetRawText());
        Assert.True(config.TryGetProperty("orderSyncEnabled", out var capability), "响应缺少订单同步能力字段。");
        Assert.Equal(expected, capability.GetBoolean());
        using var status = await http.GetAsync("/api/erp/shops/30/order-sync", ct);
        Assert.Equal(expected ? HttpStatusCode.NoContent : HttpStatusCode.NotFound, status.StatusCode);

        using var start = await http.PostAsync("/api/erp/shops/30/order-sync", null, ct);
        Assert.Equal(expected ? HttpStatusCode.Accepted : HttpStatusCode.NotFound, start.StatusCode);
        if (expected)
        {
            using var conflict = await http.PostAsync("/api/erp/shops/31/order-sync", null, ct);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }
    }
}
