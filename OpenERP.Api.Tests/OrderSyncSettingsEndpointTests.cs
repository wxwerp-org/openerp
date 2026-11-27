using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenERP.Api.Authentication;
using OpenERP.Api.Orders;
using OpenERP.Api.Orders.Sources;
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
        using var response = await app.Http.PutAsJsonAsync("/api/order-sync/settings",
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
        using var response = await app.Http.PutAsJsonAsync("/api/order-sync/settings",
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
        using var saved = await app.Http.PutAsJsonAsync("/api/order-sync/settings",
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

        using var loaded = await app.Http.GetAsync("/api/order-sync/settings", TestContext.Current.CancellationToken);
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
        using var response = await app.Http.PutAsJsonAsync("/api/order-sync/settings",
            new { enabled = false, shopSids = Array.Empty<int>() }, TestContext.Current.CancellationToken);

        // Then 关闭状态可以保存空选店。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var savedSettings = app.Store.Saved;
        Assert.NotNull(savedSettings);
        Assert.False(savedSettings.Enabled);
        Assert.Empty(savedSettings.ShopSids);
    }

    [Fact]
    public async Task Shop_selection_reports_an_unavailable_local_directory()
    {
        // Given 本地数据库不可连接。
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        await using var app = await StartAppAsync(connectionString: $"Host=127.0.0.1;Port={port};Database=test;Username=test;Timeout=1");

        // When 读取选店目录，Then 返回可重试的本地依赖错误。
        var ct = TestContext.Current.CancellationToken;
        using var response = await app.Http.GetAsync("/api/order-sync/settings/shops", ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("本地店铺数据暂时不可用。", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Saving_requires_both_settings_fields()
    {
        // Given 定时同步接口已开放。
        await using var app = await StartAppAsync();

        // When 请求缺少 shopSids 或整个响应体为空。
        using var missingShops = await app.Http.PutAsJsonAsync("/api/order-sync/settings",
            new { enabled = true }, TestContext.Current.CancellationToken);
        using var empty = await app.Http.PutAsJsonAsync("/api/order-sync/settings",
            new { }, TestContext.Current.CancellationToken);

        // Then 均拒绝保存，避免用默认值静默清空已有设置。
        Assert.Equal(HttpStatusCode.BadRequest, missingShops.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Null(app.Store.Saved);
    }

    [Fact]
    public async Task Saving_maps_store_failures_to_server_error()
    {
        // Given 设置存储保存失败。
        await using var app = await StartAppAsync();
        app.Store.FailSave = true;

        // When 保存关闭状态。
        using var response = await app.Http.PutAsJsonAsync("/api/order-sync/settings",
            new { enabled = false, shopSids = Array.Empty<int>() }, TestContext.Current.CancellationToken);

        // Then 返回可读的服务器错误，而不是 200。
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains("保存定时同步设置失败", body.GetProperty("title").GetString());
    }

    private static async Task<TestApp> StartAppAsync(string? connectionString = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseOpenErpPostgres(connectionString));
        ErpEndpointTestAuth.Configure(builder);
        builder.Services.AddSingleton<IOrderSyncSettingsStore>(new TestSettingsStore());
        builder.Services.AddSingleton<OrderSyncJobs>();
        builder.Services.AddSingleton(new PlatformOrderSources([new TestPlatformOrderSource()]));
        builder.Services.AddSingleton<OrderSyncScheduler>();
        builder.Services.AddOptions<OrderSyncSchedulerOptions>();
        var app = builder.Build();
        app.MapOrderSync();
        await app.StartAsync(cancellationToken);
        var http = ErpEndpointTestAuth.CreateClient(app, UserAccount.AdministratorRole);
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
