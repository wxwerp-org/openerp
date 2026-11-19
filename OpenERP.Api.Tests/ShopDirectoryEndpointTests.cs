using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpResult;
using OpenERP.Api.Erp;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class ShopDirectoryEndpointTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string _databaseName = $"openerp_shop_directory_test_{Guid.NewGuid():N}";
    private string _adminConnectionString = "";
    private string _connectionString = "";
    private bool _databaseCreated;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _adminConnectionString = await fixture.GetPostgresConnectionStringAsync(cancellationToken);
        _connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""CREATE DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _databaseCreated = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_databaseCreated)
            return;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""DROP DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Shops_endpoint_reads_local_rows_with_paging_and_global_summary()
    {
        // Given 本地有三家不同授权状态的店铺和一条同步结果。
        var ct = TestContext.Current.CancellationToken;
        var lastSyncedAt = new DateTimeOffset(2026, 11, 18, 5, 20, 0, TimeSpan.Zero);
        await SeedAsync(lastSyncedAt, ct);
        await using var app = await StartAppAsync(ct);

        // When 打开默认分页。
        using var response = await app.Http.GetAsync("/api/erp/shops?page=1&pageSize=2", ct);

        // Then 返回本地分页、字符串 SID 与全量汇总。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, body.GetProperty("pageCount").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());

        var summary = body.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("valid").GetInt32());
        Assert.Equal(1, summary.GetProperty("invalid").GetInt32());
        Assert.Equal(1, summary.GetProperty("unknown").GetInt32());
        Assert.Equal(lastSyncedAt, summary.GetProperty("lastSyncedAt").GetDateTimeOffset());
        Assert.Equal(5, summary.GetProperty("syncVersion").GetInt64());

        var shops = body.GetProperty("items").EnumerateArray().ToArray();
        var alpha = Assert.Single(shops, shop => shop.GetProperty("name").GetString() == "Alpha 店");
        Assert.Equal("42", alpha.GetProperty("sid").GetString());
        Assert.Equal("TemuPop", alpha.GetProperty("platform").GetString());
        Assert.Equal("US", alpha.GetProperty("region").GetString());
        Assert.Equal("member-42", alpha.GetProperty("memberId").GetString());
        Assert.True(alpha.GetProperty("isTokenOk").GetBoolean());
        Assert.Equal(JsonValueKind.Null, alpha.GetProperty("tokenError").ValueKind);
        Assert.Equal(lastSyncedAt.AddMinutes(-5), alpha.GetProperty("syncedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Status_filter_changes_items_but_not_summary()
    {
        // Given 本地有效、无效、未知各一家。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var app = await StartAppAsync(ct);

        // When 只筛选无效店铺。
        using var response = await app.Http.GetAsync("/api/erp/shops?status=invalid", ct);

        // Then 列表只剩无效店，汇总仍统计全部店铺。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Beta 店", item.GetProperty("name").GetString());
        Assert.False(item.GetProperty("isTokenOk").GetBoolean());
        Assert.Equal(1, body.GetProperty("summary").GetProperty("valid").GetInt32());
        Assert.Equal(1, body.GetProperty("summary").GetProperty("invalid").GetInt32());
        Assert.Equal(1, body.GetProperty("summary").GetProperty("unknown").GetInt32());
    }

    [Fact]
    public async Task Platform_and_keyword_filters_scope_summary()
    {
        // Given 本地有三家店铺。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var app = await StartAppAsync(ct);

        // When 按平台筛选 TemuPop。
        using var platformResponse = await app.Http.GetAsync("/api/erp/shops?platform=TemuPop", ct);
        Assert.Equal(HttpStatusCode.OK, platformResponse.StatusCode);
        var platformBody = await platformResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, platformBody.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, platformBody.GetProperty("summary").GetProperty("valid").GetInt32());
        Assert.Equal(0, platformBody.GetProperty("summary").GetProperty("invalid").GetInt32());
        Assert.Equal(1, platformBody.GetProperty("summary").GetProperty("unknown").GetInt32());

        // When 按关键词匹配店名。
        using var nameResponse = await app.Http.GetAsync("/api/erp/shops?keyword=Alpha", ct);
        var nameBody = await nameResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, nameBody.GetProperty("totalCount").GetInt32());
        Assert.Equal("Alpha 店", Assert.Single(nameBody.GetProperty("items").EnumerateArray())
            .GetProperty("name").GetString());

        // When 按 SID 匹配。
        using var sidResponse = await app.Http.GetAsync("/api/erp/shops?keyword=6", ct);
        var sidBody = await sidResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("6", Assert.Single(sidBody.GetProperty("items").EnumerateArray())
            .GetProperty("sid").GetString());
    }

    [Fact]
    public async Task Invalid_filters_and_paging_are_rejected_or_clamped()
    {
        // Given 本地有三家店铺。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var app = await StartAppAsync(ct);

        // When 传入非法状态、页码和每页条数，以及超出范围的页码。
        using var status = await app.Http.GetAsync("/api/erp/shops?status=expired", ct);
        using var numericStatus = await app.Http.GetAsync("/api/erp/shops?status=1", ct);
        using var page = await app.Http.GetAsync("/api/erp/shops?page=0", ct);
        using var pageSize = await app.Http.GetAsync("/api/erp/shops?pageSize=101", ct);
        using var tooFar = await app.Http.GetAsync("/api/erp/shops?page=9&pageSize=2", ct);

        // Then 非法参数返回 400，超范围页码钳制到最后一页。
        Assert.Equal(HttpStatusCode.BadRequest, status.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, numericStatus.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, page.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, pageSize.StatusCode);
        var clamped = await tooFar.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, clamped.GetProperty("page").GetInt32());
        Assert.Equal(1, clamped.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Platforms_endpoint_aggregates_local_counts_and_metadata()
    {
        // Given 本地有 TemuPop 两家、AliExpress 一家，并保存平台展示名。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var app = await StartAppAsync(ct);

        // When 读取平台目录。
        using var response = await app.Http.GetAsync("/api/erp/platforms", ct);

        // Then 平台来自本地目录，计数来自本地店铺，不访问上游。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var platforms = body.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(["TemuPop", "AliExpress"], platforms.Select(platform => platform.GetProperty("name").GetString()));
        Assert.Equal([2, 1], platforms.Select(platform => platform.GetProperty("shopCount").GetInt32()));
        Assert.Equal("Temu半托管", platforms[0].GetProperty("displayName").GetString());
        Assert.Equal("icon-temupop", platforms[0].GetProperty("icon").GetString());
    }

    [Fact]
    public async Task Shop_sync_endpoint_reads_database_when_idle_and_memory_when_running()
    {
        // Given 数据库中已有上次成功结果，任务当前空闲。
        var ct = TestContext.Current.CancellationToken;
        var completedAt = ToDatabasePrecision(DateTimeOffset.UtcNow.AddMinutes(-12));
        await SeedAsync(completedAt, ct);
        await using var idleApp = await StartAppAsync(ct);

        // When 查询同步状态。
        using var idleResponse = await idleApp.Http.GetAsync("/api/erp/shop-sync", ct);

        // Then 返回数据库中的最近一次结果。
        Assert.Equal(HttpStatusCode.OK, idleResponse.StatusCode);
        var idle = await idleResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(idle.GetProperty("running").GetBoolean());
        Assert.Equal(completedAt, idle.GetProperty("completedAt").GetDateTimeOffset());
        Assert.Equal(3, idle.GetProperty("total").GetInt32());
        Assert.Equal(3, idle.GetProperty("checked").GetInt32());
        Assert.Equal(1, idle.GetProperty("valid").GetInt32());
        Assert.Equal(1, idle.GetProperty("invalid").GetInt32());
        Assert.Equal(1, idle.GetProperty("unknown").GetInt32());
        Assert.Equal(JsonValueKind.Null, idle.GetProperty("error").ValueKind);
        Assert.Equal(5, idle.GetProperty("version").GetInt64());

        // Given 任务正在运行。
        var running = new ShopSyncStatus(true, completedAt.AddMinutes(1), completedAt, 10, 4, 2, 1, 1, null, 6);
        await using var runningApp = await StartAppAsync(ct, new FakeShopSyncJob(running));

        // When 再次查询。
        using var runningResponse = await runningApp.Http.GetAsync("/api/erp/shop-sync", ct);

        // Then 返回内存中的实时进度。
        var progress = await runningResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(progress.GetProperty("running").GetBoolean());
        Assert.Equal(10, progress.GetProperty("total").GetInt32());
        Assert.Equal(4, progress.GetProperty("checked").GetInt32());
        Assert.Equal(6, progress.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Empty_database_returns_empty_page_and_platforms()
    {
        // Given 本地还没有同步过任何店铺。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        await using var app = await StartAppAsync(ct);

        // When 读取店铺与平台。
        using var shopsResponse = await app.Http.GetAsync("/api/erp/shops", ct);
        using var platformsResponse = await app.Http.GetAsync("/api/erp/platforms", ct);

        // Then 返回空页与空平台，不报错。
        Assert.Equal(HttpStatusCode.OK, shopsResponse.StatusCode);
        var shops = await shopsResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(0, shops.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, shops.GetProperty("pageCount").GetInt32());
        Assert.Empty(shops.GetProperty("items").EnumerateArray());
        Assert.Equal(0, shops.GetProperty("summary").GetProperty("valid").GetInt32());
        Assert.Equal(JsonValueKind.Null, shops.GetProperty("summary").GetProperty("lastSyncedAt").ValueKind);

        var platforms = await platformsResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Empty(platforms.GetProperty("items").EnumerateArray());
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync(cancellationToken);
    }

    private async Task SeedAsync(DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        await MigrateAsync(cancellationToken);
        await using var db = CreateDbContext();
        db.Shops.AddRange(
            new Shop
            {
                Sid = 42, Name = "Alpha 店", Platform = "TemuPop", Region = "US", MemberId = "member-42",
                IsTokenOk = true, SyncedAt = completedAt.AddMinutes(-5)
            },
            new Shop
            {
                Sid = 6, Name = "Beta 店", Platform = "AliExpress", Region = "US",
                IsTokenOk = false, SyncedAt = completedAt.AddMinutes(-5)
            },
            new Shop
            {
                Sid = 9, Name = "Gamma 店", Platform = "TemuPop", Region = "US",
                IsTokenOk = null, TokenError = "状态读取失败", SyncedAt = completedAt.AddMinutes(-5)
            });
        db.ShopPlatforms.AddRange(
            new ShopPlatform
            {
                Name = "TemuPop", DisplayName = "Temu半托管", Icon = "icon-temupop", IsProduction = true
            },
            new ShopPlatform
            {
                Name = "AliExpress", DisplayName = "速卖通", Icon = "icon-aliexpress", IsProduction = true
            });
        db.ShopSyncStates.Add(new ShopSyncState
        {
            Id = 1, Version = 5, LastCompletedAt = completedAt, Total = 3, Valid = 1, Invalid = 1, Unknown = 1
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset ToDatabasePrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % 10));

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options);

    private async Task<TestApp> StartAppAsync(
        CancellationToken cancellationToken, IShopSyncJob? job = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Staging" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_connectionString));
        builder.Services.AddSingleton<IErpClient>(new TestErpClient());
        builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore("test-token"));
        builder.Services.AddSingleton(job ?? new FakeShopSyncJob());
        var app = builder.Build();
        app.MapErp();
        await app.StartAsync(cancellationToken);
        var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        return new TestApp(app, http);
    }

    private sealed class FakeShopSyncJob(ShopSyncStatus? status = null) : IShopSyncJob
    {
        public ShopSyncStatus Status { get; } = status ?? new(false, null, null, 0, 0, 0, 0, 0, null, 0);

        public Task<OpResult<ShopSyncStatus>> TryStartAsync(CancellationToken cancellationToken) =>
            Task.FromResult<OpResult<ShopSyncStatus>>(OpResults.Err("测试中不触发同步。"));
    }

    private sealed class TestApp(WebApplication app, HttpClient http) : IAsyncDisposable
    {
        public HttpClient Http => http;

        public async ValueTask DisposeAsync()
        {
            http.Dispose();
            await app.DisposeAsync();
        }
    }
}
