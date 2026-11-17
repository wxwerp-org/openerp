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
        builder.Services.AddSingleton<IOrderSyncSettingsStore>(new TestSettingsStore());
        builder.Services.AddOptions<OrderSyncSchedulerOptions>();
        builder.Services.AddSingleton<OrderSyncScheduler>();
        await using var app = builder.Build();
        app.MapErp();
        await app.StartAsync(ct);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        // When 浏览器查询 ERP 配置。
        using var configResponse = await http.GetAsync("/api/erp/config", ct);
        if (!configResponse.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException(await configResponse.Content.ReadAsStringAsync(ct));
        var config = await configResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

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
        else
        {
            using var settings = await http.GetAsync("/api/erp/order-sync-settings", ct);
            Assert.Equal(HttpStatusCode.NotFound, settings.StatusCode);
            using var selection = await http.GetAsync("/api/erp/order-sync-settings/shops", ct);
            Assert.Equal(HttpStatusCode.NotFound, selection.StatusCode);
            using var save = await http.PutAsync("/api/erp/order-sync-settings", null, ct);
            Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
        }
    }
}
