using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpResult;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderSyncSettingsEndpointTests
{
    [Fact]
    public async Task Saving_enabled_settings_requires_at_least_one_shop()
    {
        // Given 定时同步接口已开放，设置存储为空。
        await using var app = await StartAppAsync();

        // When 尝试在未选择店铺时启用。
        using var response = await app.Http.PutAsJsonAsync("/api/erp/order-sync-settings",
            new { enabled = true, shopSids = Array.Empty<int>() }, TestContext.Current.CancellationToken);

        // Then 拒绝保存且不写入设置。
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(app.Store.Saved);
    }

    [Fact]
    public async Task Saving_rejects_non_positive_shop_sids()
    {
        // Given 定时同步接口已开放。
        await using var app = await StartAppAsync();

        // When 提交包含非法 SID 的设置。
        using var response = await app.Http.PutAsJsonAsync("/api/erp/order-sync-settings",
            new { enabled = true, shopSids = new[] { 30, 0 } }, TestContext.Current.CancellationToken);

        // Then 整个请求被拒绝，不保存部分设置。
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(app.Store.Saved);
    }

    [Fact]
    public async Task Saving_deduplicates_shops_and_get_returns_the_fixed_interval()
    {
        // Given 定时同步接口已开放。
        await using var app = await StartAppAsync();

        // When 保存启用状态、重复 SID 与另一家店。
        using var saved = await app.Http.PutAsJsonAsync("/api/erp/order-sync-settings",
            new { enabled = true, shopSids = new[] { 644928, 30, 644928 } }, TestContext.Current.CancellationToken);

        // Then 保存成功、店铺去重，GET 同时返回只读周期。
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var savedBody = await saved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(savedBody.GetProperty("enabled").GetBoolean());
        Assert.Equal([30, 644928], savedBody.GetProperty("shopSids").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(30, savedBody.GetProperty("intervalMinutes").GetInt32());
        var savedSettings = app.Store.Saved;
        Assert.NotNull(savedSettings);
        Assert.True(savedSettings.Enabled);
        Assert.Equal([30, 644928], savedSettings.ShopSids);

        using var loaded = await app.Http.GetAsync("/api/erp/order-sync-settings", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
        var loadedBody = await loaded.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(loadedBody.GetProperty("enabled").GetBoolean());
        Assert.Equal([30, 644928], loadedBody.GetProperty("shopSids").EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public async Task Saving_disabled_settings_allows_an_empty_selection()
    {
        // Given 定时同步接口已开放。
        await using var app = await StartAppAsync();

        // When 关闭定时同步并清空店铺。
        using var response = await app.Http.PutAsJsonAsync("/api/erp/order-sync-settings",
            new { enabled = false, shopSids = Array.Empty<int>() }, TestContext.Current.CancellationToken);

        // Then 关闭状态可以保存空选店。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var savedSettings = app.Store.Saved;
        Assert.NotNull(savedSettings);
        Assert.False(savedSettings.Enabled);
        Assert.Empty(savedSettings.ShopSids);
    }

    [Fact]
    public async Task Shop_selection_returns_every_visible_shop_without_status_queries()
    {
        // Given 当前 Token 可见两家店。
        var erp = new TestErpClient
        {
            GetAllShops = (_, _) => Task.FromResult(OpResults.Ok<IReadOnlyList<ErpShop>>([
                new("30", "店铺三十", "TemuPop", "US"),
                new("644928", "店铺六四四九二八", "TemuPop")
            ]))
        };
        await using var app = await StartAppAsync(erp);

        // When 店铺页读取选店列表。
        using var response = await app.Http.GetAsync("/api/erp/order-sync-settings/shops", TestContext.Current.CancellationToken);

        // Then 返回全部店铺的基础信息，不依赖授权状态查询。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(["30", "644928"], items.Select(item => item.GetProperty("sid").GetString()));
        Assert.Equal(["店铺三十", "店铺六四四九二八"], items.Select(item => item.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Shop_selection_returns_503_without_an_erp_token()
    {
        // Given 私有 ERP Token 未配置。
        var erp = new TestErpClient { Configuration = new(false, "https://erp.test") };
        await using var app = await StartAppAsync(erp);

        // When 读取选店列表。
        using var response = await app.Http.GetAsync("/api/erp/order-sync-settings/shops", TestContext.Current.CancellationToken);

        // Then 明确提示配置缺失。
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Shop_selection_maps_erp_failures_to_bad_gateway()
    {
        // Given 上游店铺列表读取失败。
        var erp = new TestErpClient
        {
            GetAllShops = (_, _) => Task.FromResult<OpResult<IReadOnlyList<ErpShop>>>(OpResults.Err("ERP 请求超时，请重试。"))
        };
        await using var app = await StartAppAsync(erp);

        // When 读取选店列表。
        using var response = await app.Http.GetAsync("/api/erp/order-sync-settings/shops", TestContext.Current.CancellationToken);

        // Then 返回可读的上游失败，而不是 500。
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Saving_requires_both_settings_fields()
    {
        // Given 定时同步接口已开放。
        await using var app = await StartAppAsync();

        // When 请求缺少 shopSids 或整个响应体为空。
        using var missingShops = await app.Http.PutAsJsonAsync("/api/erp/order-sync-settings",
            new { enabled = true }, TestContext.Current.CancellationToken);
        using var empty = await app.Http.PutAsJsonAsync("/api/erp/order-sync-settings",
            new { }, TestContext.Current.CancellationToken);

        // Then 均拒绝保存，避免用默认值静默清空已有设置。
        Assert.Equal(HttpStatusCode.BadRequest, missingShops.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Null(app.Store.Saved);
    }

    [Fact]
    public async Task Enabling_requires_a_configured_erp_token()
    {
        // Given 私有 ERP Token 未配置。
        var erp = new TestErpClient { Configuration = new(false, "https://erp.test") };
        await using var app = await StartAppAsync(erp);

        // When 尝试启用定时同步。
        using var response = await app.Http.PutAsJsonAsync("/api/erp/order-sync-settings",
            new { enabled = true, shopSids = new[] { 30 } }, TestContext.Current.CancellationToken);

        // Then 与手动同步一致，返回配置缺失且不保存。
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(app.Store.Saved);
    }

    [Fact]
    public async Task Saving_maps_store_failures_to_server_error()
    {
        // Given 设置存储保存失败。
        await using var app = await StartAppAsync();
        app.Store.FailSave = true;

        // When 保存关闭状态。
        using var response = await app.Http.PutAsJsonAsync("/api/erp/order-sync-settings",
            new { enabled = false, shopSids = Array.Empty<int>() }, TestContext.Current.CancellationToken);

        // Then 返回可读的服务器错误，而不是 200。
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("保存定时同步设置失败", body.GetProperty("title").GetString());
    }

    private static async Task<TestApp> StartAppAsync(TestErpClient? erp = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton<IErpClient>(erp ?? new TestErpClient());
        builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore());
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IOrderSyncSettingsStore>(new TestSettingsStore());
        builder.Services.AddSingleton<OrderSyncJobs>();
        builder.Services.AddSingleton<OrderSyncScheduler>();
        builder.Services.AddOptions<OrderSyncSchedulerOptions>();
        var app = builder.Build();
        app.MapErp();
        await app.StartAsync(cancellationToken);
        var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        return new TestApp(app, http, (TestSettingsStore)app.Services.GetRequiredService<IOrderSyncSettingsStore>());
    }

    private sealed class TestApp(WebApplication app, HttpClient http, TestSettingsStore store) : IAsyncDisposable
    {
        public HttpClient Http => http;

        public TestSettingsStore Store => store;

        public async ValueTask DisposeAsync()
        {
            http.Dispose();
            await app.DisposeAsync();
        }
    }
}
