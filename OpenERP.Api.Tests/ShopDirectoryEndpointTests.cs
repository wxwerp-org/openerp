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
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Reports;
using OpenERP.Core.Orders;
using OpenERP.Core.Reports;
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
        await using var app = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

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
        Assert.Equal(8, alpha.EnumerateObject().Count());
    }

    [Fact]
    public async Task Status_filter_changes_items_but_not_summary()
    {
        // Given 本地有效、无效、未知各一家。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var app = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

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
        await using var app = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

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
        await using var app = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

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
        await using var app = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

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
        await using var idleApp = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

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
        await using var runningApp = await StartAppAsync(
            ct, new FakeShopSyncJob(running), role: UserAccount.AdministratorRole);

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
        await using var app = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

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

    [Fact]
    public async Task Employee_without_assignments_gets_empty_directory_and_platforms()
    {
        // Given 本地有三家不同授权状态的店铺和一条同步结果，调用方是普通员工。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var app = await StartAppAsync(ct, role: UserAccount.UserRole);

        // When 按默认分页读取店铺目录。
        using var response = await app.Http.GetAsync("/api/erp/shops?page=1&pageSize=2", ct);

        // Then 两个入口都返回空选项，目录仍保留分页字段。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, body.GetProperty("pageCount").GetInt32());
        Assert.False(body.TryGetProperty("summary", out _));
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        using var platforms = await app.Http.GetAsync("/api/erp/platforms", ct);
        Assert.Equal(HttpStatusCode.OK, platforms.StatusCode);
        var catalog = await platforms.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Empty(catalog.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Employee_without_assignments_gets_empty_orders_and_options()
    {
        // Given 本地已有一张订单，员工尚未分配店铺。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await SeedOrderAsync(ct);
        await using var app = await StartAppAsync(ct);

        // When 查看订单；Then 明细、总数和选项均为空。
        using var orders = await app.Http.GetAsync("/api/orders", ct);
        Assert.Equal(HttpStatusCode.OK, orders.StatusCode);
        var listBody = await orders.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(0, listBody.GetProperty("total").GetInt64());
        Assert.Empty(listBody.GetProperty("items").EnumerateArray());
        foreach (var field in new[] { "shops", "platforms", "statuses" })
            Assert.Empty(listBody.GetProperty("options").GetProperty(field).EnumerateArray());
    }

    [Fact]
    public async Task Employee_without_assignments_gets_empty_workbench_totals_and_report_options()
    {
        // Given 本地已有一张订单，员工尚未分配店铺。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await SeedOrderAsync(ct);
        await using var app = await StartAppAsync(ct);

        // When 查看工作台报表；Then 聚合、明细与选项均为空。
        using var report = await app.Http.GetAsync("/api/reports/order-snapshot", ct);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportBody = await report.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(0, reportBody.GetProperty("summary").GetProperty("orderCount").GetInt64());
        Assert.Equal(0, reportBody.GetProperty("summary").GetProperty("itemQuantity").GetInt64());
        foreach (var field in new[] { "orders", "groups", "shops", "platforms" })
            Assert.Empty(reportBody.GetProperty(field).EnumerateArray());
    }

    [Theory]
    [InlineData("/api/orders?shopSid=42")]
    [InlineData("/api/orders?shopSid=42&platform=TemuPop")]
    [InlineData("/api/reports/order-snapshot?shops=wxw-erp%2FTemuPop%2F42")]
    public async Task Employee_explicitly_requesting_an_unassigned_shop_is_forbidden(string url)
    {
        // Given 店铺有本地订单，员工未分配到该店；When 使用公开筛选参数指定该店。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await SeedOrderAsync(ct);
        await using var app = await StartAppAsync(ct);
        using var response = await app.Http.GetAsync(url, ct);

        // Then 返回 403，不能按全量过滤后返回该店资料。
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_directory_and_platform_counts_use_assignments_and_expose_shop_status()
    {
        // Given 员工仅负责 Gamma，目录内另有同平台未分配店与其它平台。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        var userId = await SeedEmployeeAsync(ct, ("TemuPop", 9), ("AliExpress", 42));
        await using var app = await StartAppAsync(ct, userId: userId);

        // When 读取目录以及筛选选项；Then 只有 Gamma 的授权状态、失败原因和刷新时间可见。
        using var response = await app.Http.GetAsync("/api/erp/shops?platform=TemuPop&keyword=店", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.False(page.TryGetProperty("summary", out _));
        var shop = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("9", shop.GetProperty("sid").GetString());
        Assert.Equal(JsonValueKind.Null, shop.GetProperty("isTokenOk").ValueKind);
        Assert.Equal("状态读取失败", shop.GetProperty("tokenError").GetString());
        Assert.Equal(JsonValueKind.String, shop.GetProperty("syncedAt").ValueKind);
        Assert.False(shop.TryGetProperty("memberId", out _));
        using var platforms = await app.Http.GetAsync("/api/erp/platforms", ct);
        Assert.Equal(HttpStatusCode.OK, platforms.StatusCode);
        var catalog = await platforms.Content.ReadFromJsonAsync<JsonElement>(ct);
        var platform = Assert.Single(catalog.GetProperty("items").EnumerateArray());
        Assert.Equal("TemuPop", platform.GetProperty("name").GetString());
        Assert.Equal(1, platform.GetProperty("shopCount").GetInt32());
    }

    [Fact]
    public async Task Assigned_history_uses_the_composite_shop_key_across_orders_reports_and_revocation()
    {
        // Given SID 42 仍在 TemuPop 目录，但员工分配的是 AliExpress 的同 SID 历史店。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await SeedOrderAsync(ct);
        await SeedOrderAsync(ct, "AliExpress", "待处理", 3);
        var userId = await SeedEmployeeAsync(ct, ("AliExpress", 42));
        await using var employee = await StartAppAsync(ct, userId: userId);
        await using var administrator = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

        // When 查看员工订单；Then 历史店可按 SID 查询，订单名称、平台和状态均无其它平台信息。
        using var orders = await employee.Http.GetAsync("/api/orders?shopSid=42", ct);
        Assert.Equal(HttpStatusCode.OK, orders.StatusCode);
        var list = await orders.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, list.GetProperty("total").GetInt64());
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("AliExpress", row.GetProperty("platform").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("shopName").ValueKind);
        Assert.Equal("待处理", Assert.Single(list.GetProperty("options").GetProperty("statuses").EnumerateArray()).GetString());
        Assert.Equal("AliExpress", Assert.Single(list.GetProperty("options").GetProperty("platforms").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null,
            Assert.Single(list.GetProperty("options").GetProperty("shops").EnumerateArray()).GetProperty("name").ValueKind);
        using var otherPlatform = await employee.Http.GetAsync("/api/orders?shopSid=42&platform=TemuPop", ct);
        Assert.Equal(HttpStatusCode.Forbidden, otherPlatform.StatusCode);

        // When 读取工作台报表；Then 统计和明细与管理员选择该历史店时一致。
        const string historicalReport = "/api/reports/order-snapshot?shops=wxw-erp%2FAliExpress%2F42";
        using var report = await employee.Http.GetAsync("/api/reports/order-snapshot", ct);
        using var filtered = await administrator.Http.GetAsync(historicalReport, ct);
        using var selected = await employee.Http.GetAsync(historicalReport, ct);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var summary = await report.Content.ReadFromJsonAsync<JsonElement>(ct);
        var comparison = await filtered.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(comparison.GetProperty("summary").GetRawText(), summary.GetProperty("summary").GetRawText());
        Assert.Equal(3, summary.GetProperty("summary").GetProperty("itemQuantity").GetInt64());
        Assert.Equal(comparison.GetProperty("groups").GetRawText(), summary.GetProperty("groups").GetRawText());
        Assert.Equal(comparison.GetProperty("orders").GetRawText(), summary.GetProperty("orders").GetRawText());
        var selectedReport = await selected.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(summary.GetProperty("summary").GetRawText(), selectedReport.GetProperty("summary").GetRawText());
        Assert.Equal("AliExpress", Assert.Single(summary.GetProperty("platforms").EnumerateArray()).GetString());
        Assert.Equal("wxw-erp/AliExpress/42", Assert.Single(summary.GetProperty("shops").EnumerateArray()).GetProperty("key").GetString());
        using var mixed = await employee.Http.GetAsync(
            "/api/reports/order-snapshot?shops=wxw-erp%2FAliExpress%2F42&shops=wxw-erp%2FTemuPop%2F42", ct);
        Assert.Equal(HttpStatusCode.Forbidden, mixed.StatusCode);

        // When 管理员撤销该分配；Then 下一次查询不再允许指定该店。
        await using var db = CreateDbContext();
        await db.UserShopAssignments.Where(item => item.UserId == userId).ExecuteDeleteAsync(ct);
        using var revoked = await employee.Http.GetAsync(historicalReport, ct);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [Fact]
    public async Task Employee_carrying_status_key_is_forbidden_before_parameter_validation()
    {
        // Given 本地有三家不同授权状态的店铺，调用方是普通员工。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var app = await StartAppAsync(ct, role: UserAccount.UserRole);

        // When 员工携带 status 键，取值包括有效、未知、空值与纯空白。
        using var invalid = await app.Http.GetAsync("/api/erp/shops?status=invalid", ct);
        using var valid = await app.Http.GetAsync("/api/erp/shops?status=valid", ct);
        using var unknown = await app.Http.GetAsync("/api/erp/shops?status=unknown", ct);
        using var blank = await app.Http.GetAsync("/api/erp/shops?status=", ct);
        using var whitespace = await app.Http.GetAsync("/api/erp/shops?status=%20%20", ct);

        // Then 只要携带 status 就返回 403 ProblemDetails，空值与空白也不例外。
        foreach (var response in new[] { invalid, valid, unknown, blank, whitespace })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        // When 员工在非法分页参数之外再携带 status；Then 角色检查先于参数绑定，仍返回 403。
        using var bindingFailure = await app.Http.GetAsync("/api/erp/shops?page=abc&status=valid", ct);
        Assert.Equal(HttpStatusCode.Forbidden, bindingFailure.StatusCode);

        // When 员工改用平台、关键词和分页筛选。
        using var filtered = await app.Http.GetAsync("/api/erp/shops?platform=TemuPop&keyword=店&pageSize=10", ct);

        // Then 不携带 status 时非状态筛选与分页仍然可用。
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var body = await filtered.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.False(body.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task Administrator_status_and_paging_semantics_are_unchanged()
    {
        // Given 本地有三家不同授权状态的店铺，调用方是管理员。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var app = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

        // When 管理员分别携带空状态、有效状态、非法状态与非法分页参数。
        using var blank = await app.Http.GetAsync("/api/erp/shops?status=", ct);
        using var whitespace = await app.Http.GetAsync("/api/erp/shops?status=%20", ct);
        using var valid = await app.Http.GetAsync("/api/erp/shops?status=valid", ct);
        using var expired = await app.Http.GetAsync("/api/erp/shops?status=expired", ct);
        using var badPage = await app.Http.GetAsync("/api/erp/shops?page=abc", ct);
        using var emptyPage = await app.Http.GetAsync("/api/erp/shops?page=", ct);

        // Then 空状态视为未筛选，非法状态与非法分页仍返回 400。
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        Assert.Equal(HttpStatusCode.OK, whitespace.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyPage.StatusCode);
    }

    [Fact]
    public async Task Shop_sync_status_is_administrator_only()
    {
        // Given 本地已有上次同步结果，普通员工与管理员分别登录。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow, ct);
        await using var employeeApp = await StartAppAsync(ct, role: UserAccount.UserRole);
        await using var adminApp = await StartAppAsync(ct, role: UserAccount.AdministratorRole);

        // When 两者分别读取目录同步状态。
        using var employee = await employeeApp.Http.GetAsync("/api/erp/shop-sync", ct);
        using var admin = await adminApp.Http.GetAsync("/api/erp/shop-sync", ct);

        // Then 员工被拒绝，管理员仍可读取。
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task Offline_order_schedule_uses_persisted_shops_and_saves_selection()
    {
        // Given wxw 已离线一天，应用重新启动，数据库保留上次同步的多平台店铺。
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(DateTimeOffset.UtcNow.AddDays(-1), ct);
        var upstreamCalls = 0;
        var offline = new TestErpClient
        {
            GetAllShops = (_, _, _) =>
            {
                Interlocked.Increment(ref upstreamCalls);
                return Task.FromResult<OpResult<IReadOnlyList<ErpShop>>>(OpResults.Err("wxw 离线"));
            }
        };
        await using var app = await StartAppAsync(ct, erp: offline, role: UserAccount.AdministratorRole);

        // When 打开定时选店，Then 返回本地全部 TemuPop 店铺，包括授权状态未知的店。
        const string settings = "/api/erp/order-sync-settings";
        using var response = await app.Http.GetAsync($"{settings}/shops", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var selection = await response.Content.ReadFromJsonAsync<OrderSyncShopSelection>(ct);
        Assert.NotNull(selection);
        Assert.Equal([new("42", "Alpha 店"), new("9", "Gamma 店")], selection.Items);

        // When 选店并启用，Then 下次读取保留已保存的设置。
        using var saved = await app.Http.PutAsJsonAsync(settings, new { enabled = true, shopSids = new[] { 9 } }, ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var loaded = await app.Http.GetFromJsonAsync<OrderSyncSettingsResponse>(settings, ct);
        Assert.NotNull(loaded);
        Assert.True(loaded.Enabled);
        Assert.Equal([9], loaded.ShopSids);
        Assert.Equal(0, upstreamCalls);
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
            Version = 5, LastCompletedAt = completedAt, Total = 3, Valid = 1, Invalid = 1, Unknown = 1
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset ToDatabasePrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % 10));

    private async Task SeedOrderAsync(CancellationToken ct, string platform = "TemuPop", string status = "已发货", int quantity = 7)
    {
        await using var db = CreateDbContext();
        var store = new OrderStore(db);
        var snapshot = new OrderSnapshot($"order-42-{platform}", $"order-42-{platform}", 42, platform, "正常", new(null, null, null, null),
            [new("PO-42", 42, "4", status)], [new(null, null, null, null, quantity, false, false)]);
        Assert.False((await store.SaveAsync([snapshot], ct)).IsErr);
    }

    private async Task<Guid> SeedEmployeeAsync(CancellationToken ct, params (string Platform, int Sid)[] shops)
    {
        await using var db = CreateDbContext();
        var user = new UserAccount { Username = "employee", Name = "员工" };
        db.Users.Add(user);
        db.UserShopAssignments.AddRange(shops.Select(shop => new UserShopAssignment
        {
            UserId = user.Id,
            Platform = shop.Platform,
            Sid = shop.Sid
        }));
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseOpenErpPostgres(_connectionString).Options);

    private async Task<TestApp> StartAppAsync(
        CancellationToken cancellationToken, IShopSyncJob? job = null, IErpClient? erp = null,
        string role = UserAccount.UserRole, Guid? userId = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseOpenErpPostgres(_connectionString));
        builder.Services.AddSingleton<IErpClient>(erp ?? new TestErpClient());
        builder.Services.AddSingleton<IErpTokenStore>(new TestErpTokenStore("test-token"));
        builder.Services.AddSingleton(job ?? new FakeShopSyncJob());
        builder.Services.AddScoped<IOrderSyncSettingsStore, OrderSyncSettingsStore>();
        builder.Services.AddSingleton<OrderSyncJobs>();
        builder.Services.AddSingleton<OrderSyncScheduler>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<OrderListQuery>();
        builder.Services.AddScoped<IOrderSnapshotReportQuery, OrderSnapshotReportQuery>();
        ErpEndpointTestAuth.Configure(builder);
        var app = builder.Build();
        app.MapErp();
        app.MapOrderList();
        app.MapOrderReports();
        await app.StartAsync(cancellationToken);
        var http = ErpEndpointTestAuth.CreateClient(app, role, userId);
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
