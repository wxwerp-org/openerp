using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Orders.Sources;
using Xunit;

namespace OpenERP.Api.Tests;

/// <summary>订单同步页的店铺列表与能力查询：店铺来自授权店铺目录，只传店铺编号即可同步，不保存任何平台凭据。</summary>
[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderSyncShopEndpointTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private const string Unsupported = "TemuPop";

    private readonly string _databaseName = $"openerp_order_sync_shop_test_{Guid.NewGuid():N}";
    private string _adminConnectionString = "";
    private string _connectionString = "";
    private bool _databaseCreated;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _adminConnectionString = await fixture.GetPostgresConnectionStringAsync(ct);
        _connectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"""CREATE DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync(ct);
        _databaseCreated = true;
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync(ct);
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
    public async Task Employee_sees_only_assigned_shops_and_unsupported_platforms_are_listed_but_flagged()
    {
        // Given 三家店：两家速卖通、一家未实现同步的 TemuPop；员工被分配其中的速卖通一家和 TemuPop 一家。
        var ct = TestContext.Current.CancellationToken;
        var supported = await SeedShopAsync("速卖通甲", TestPlatformOrderSource.DefaultPlatform, ct);
        var other = await SeedShopAsync("速卖通乙", TestPlatformOrderSource.DefaultPlatform, ct);
        var unsupported = await SeedShopAsync("暂不支持", Unsupported, ct);
        Guid userId;
        await using (var db = CreateDbContext())
        {
            var user = new UserAccount { Username = "employee", Name = "员工" };
            db.Users.Add(user);
            db.UserShopAssignments.AddRange(
                new UserShopAssignment { UserId = user.Id, Platform = TestPlatformOrderSource.DefaultPlatform, Sid = supported },
                new UserShopAssignment { UserId = user.Id, Platform = Unsupported, Sid = unsupported });
            await db.SaveChangesAsync(ct);
            userId = user.Id;
        }

        await using var admin = await StartAppAsync(UserAccount.AdministratorRole, ct);
        await using var employee = await StartAppAsync(UserAccount.UserRole, ct, userId);

        // When 员工与管理员分别读取店铺列表。
        var employeePage = await employee.Http.GetFromJsonAsync<JsonElement>("/api/order-sync/shops", ct);
        var adminPage = await admin.Http.GetFromJsonAsync<JsonElement>("/api/order-sync/shops", ct);

        // Then 员工只看到被分配的两家，管理员看到全部；平台未实现的店铺也在列表里，但标记为不支持；条目不含任何凭据字段。
        var employeeItems = employeePage.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("sid").GetInt32());
        Assert.Equal([supported, unsupported], employeeItems.Keys.Order());
        Assert.Equal(3, adminPage.GetProperty("totalCount").GetInt32());
        Assert.True(employeeItems[supported].GetProperty("syncSupported").GetBoolean());
        Assert.False(employeeItems[unsupported].GetProperty("syncSupported").GetBoolean());
        Assert.Equal(["name", "platform", "region", "sid", "syncSupported"],
            employeeItems[supported].EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var adminItems = adminPage.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("sid").GetInt32());
        Assert.True(adminItems[other].GetProperty("syncSupported").GetBoolean());
    }

    [Fact]
    public async Task Availability_reports_the_erp_token_state_and_the_supported_platforms()
    {
        // Given 订单同步可用，ERP Token 尚未配置。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(UserAccount.UserRole, ct, erpToken: "");

        // When 读取能力。Then 未配置 ERP，订单同步可用，并列出已实现的平台。
        var unconfigured = await app.Http.GetFromJsonAsync<JsonElement>("/api/order-sync/availability", ct);
        Assert.False(unconfigured.GetProperty("configured").GetBoolean());
        Assert.True(unconfigured.GetProperty("orderSyncEnabled").GetBoolean());
        Assert.Equal([TestPlatformOrderSource.DefaultPlatform],
            unconfigured.GetProperty("platforms").EnumerateArray().Select(item => item.GetString()));

        // When 配置 ERP Token 后再次读取。Then configured 为真。
        await using var configuredApp = await StartAppAsync(UserAccount.UserRole, ct, erpToken: "token");
        var configured = await configuredApp.Http.GetFromJsonAsync<JsonElement>("/api/order-sync/availability", ct);
        Assert.True(configured.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task Credential_endpoints_no_longer_exist()
    {
        // Given 管理员已登录，目录里有一家速卖通店铺。
        var ct = TestContext.Current.CancellationToken;
        var sid = await SeedShopAsync("店铺一", TestPlatformOrderSource.DefaultPlatform, ct);
        await using var app = await StartAppAsync(UserAccount.AdministratorRole, ct);

        // When 请求已删除的凭据接口。Then 找不到路由；平台凭据由旺销王托管，只用店铺编号调用。
        using var put = await app.Http.PutAsJsonAsync($"/api/order-sync/shops/{sid}/credentials", new { appKey = "k" }, ct);
        using var delete = await app.Http.DeleteAsync($"/api/order-sync/shops/{sid}/credentials", ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Directory_lookup_returns_platform_and_name_or_reports_the_shop_is_gone()
    {
        // Given 目录里有一家店铺。
        var ct = TestContext.Current.CancellationToken;
        var sid = await SeedShopAsync("店铺一", TestPlatformOrderSource.DefaultPlatform, ct);
        await using var db = CreateDbContext();
        var directory = new PlatformShopDirectory(db);

        // When 按编号查找存在和不存在的店铺。
        var found = await directory.FindAsync(sid, ct);
        var missing = await directory.FindAsync(777, ct);

        // Then 存在的返回平台与名称，不存在的给出明确错误。
        var shop = OpResultAssert.Ok(found);
        Assert.Equal(new PlatformShopRef(sid, TestPlatformOrderSource.DefaultPlatform, "店铺一"), shop);
        Assert.Equal("店铺不存在或已从授权店铺中移除。", OpResultAssert.Err(missing));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Listing_rejects_invalid_paging(string query)
    {
        // Given 管理员已登录。
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartAppAsync(UserAccount.AdministratorRole, ct);

        // When 使用非法分页读取。Then 返回 400。
        using var response = await app.Http.GetAsync($"/api/order-sync/shops?{query}", ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<int> SeedShopAsync(string name, string platform, CancellationToken ct)
    {
        await using var db = CreateDbContext();
        var shop = TestShops.New(name, platform);
        db.Shops.Add(shop);
        await db.SaveChangesAsync(ct);
        return shop.Sid;
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options);

    private async Task<TestApp> StartAppAsync(string role, CancellationToken ct, Guid? userId = null, string erpToken = "token")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_connectionString));
        builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore(erpToken));
        builder.Services.AddSingleton(new PlatformOrderSources([new TestPlatformOrderSource()]));
        ErpEndpointTestAuth.Configure(builder);
        var app = builder.Build();
        app.MapPlatformShops();
        await app.StartAsync(ct);
        var http = ErpEndpointTestAuth.CreateClient(app, role, userId);
        return new TestApp(app, http);
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
