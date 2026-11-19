using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderListEndpointTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private const string OrderPrefix = "order-list-test-";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _database = $"openerp_order_list_test_{Guid.NewGuid():N}";
    private string? _adminConnection;
    private string _connection = "";

    public async ValueTask InitializeAsync()
    {
        var adminConnection = await fixture.GetPostgresConnectionStringAsync(Ct);
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync(Ct);
        await using var create = new NpgsqlCommand($"""CREATE DATABASE "{_database}" """, connection);
        await create.ExecuteNonQueryAsync(Ct);
        _adminConnection = adminConnection;
        _connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = _database, Pooling = false }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminConnection is null)
            return;

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync(cleanup.Token);
        await using var drop = new NpgsqlCommand($"""DROP DATABASE "{_database}" """, connection);
        await drop.ExecuteNonQueryAsync(cleanup.Token);
    }

    [Fact]
    public async Task Rejects_anonymous_access_and_reads_the_local_database_without_an_erp_token()
    {
        // Given 测试宿主没有 ERP 凭证且本地保存了一张订单。
        await using var db = CreateContext();
        Assert.False(await db.ErpSettings.AnyAsync(Ct));
        await SaveAsync([Samples()[0]]);
        await VerifyAsync(async client =>
        {
            // When 匿名请求后以管理员身份再次查询订单列表。
            using var anonymous = await client.GetAsync("/api/orders", Ct);
            await AuthenticationIntegrationTests.LoginAsync(client, Ct, fixture.AdminPassword);
            using var authenticated = await client.GetAsync("/api/orders", Ct);

            // Then 匿名请求被拒绝，已登录用户可直接从本地读取资料。
            // 先检查有登录的结果，因为未注册路由时全局授权也可能返回 401。
            Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.True(authenticated.Headers.CacheControl?.NoStore);
            var body = await authenticated.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(Id("a"), Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        }, authenticate: false);
    }

    [Fact]
    public async Task Matches_literal_number_and_status_on_the_same_platform_order_without_splitting_the_main_order()
    {
        // Given 一张主单挂三个不同状态的 PO，另有其它店铺和平台的同号 PO。
        await SaveAsync(Samples());
        await VerifyAsync(async client =>
        {
            var filters = "shopSid=30&platform=TemuPop&number=%25_%5C";

            // When 筛选店铺、平台、号码和状态；Then 仅命中对应主单，保持所有关联 PO 和原始行数量。
            var body = await QueryAsync(client, $"{filters}&status={Uri.EscapeDataString("已发货")}");
            Assert.Equal(1, body.GetProperty("total").GetInt32());
            var order = Assert.Single(body.GetProperty("items").EnumerateArray());
            Assert.Equal(Id("a"), order.GetProperty("id").GetString());
            Assert.Equal(30, order.GetProperty("sid").GetInt32());
            Assert.Equal(JsonValueKind.Null, order.GetProperty("shopName").ValueKind);
            Assert.Equal("TemuPop", order.GetProperty("platform").GetString());
            Assert.Equal(2, order.GetProperty("lineCount").GetInt32());
            Assert.Equal(7, order.GetProperty("itemQuantity").GetInt64());
            Assert.Equal("2025-08-13T08:30:12", order.GetProperty("dispatched").GetString());
            var children = order.GetProperty("platformOrders").EnumerateArray()
                .Select(po => (po.GetProperty("number").GetString(), po.GetProperty("status").GetString(), po.GetProperty("matched").GetBoolean()));
            Assert.Equal([("PO-%_\\A", "已发货", true), ("PO-2", "2", false), ("PO-3", "未知", false)], children);

            // When 号码与状态只分别命中两张子单；Then 主单不命中，筛选项仍含完整本地订单。
            var empty = await QueryAsync(client, $"{filters}&status=2");
            Assert.Equal(0, empty.GetProperty("total").GetInt32());
            Assert.Empty(empty.GetProperty("items").EnumerateArray());
            var options = empty.GetProperty("options");
            var shops = options.GetProperty("shops").EnumerateArray().ToArray();
            Assert.Equal([30, 31], shops.Select(shop => shop.GetProperty("sid").GetInt32()));
            Assert.All(shops, shop => Assert.Equal(JsonValueKind.Null, shop.GetProperty("name").ValueKind));
            Assert.Equal(["OtherPlatform", "TemuPop"], TextValues(options.GetProperty("platforms")));
            Assert.Equal(["2", "已发货", "待处理", "未知"], TextValues(options.GetProperty("statuses")));

            // When 单独搜索号码或状态；Then 标记命中的子单，主单仍显示全部子单。
            var numberOnly = await QueryAsync(client, $"{filters}");
            Assert.Equal([true, false, false], Markers(Assert.Single(numberOnly.GetProperty("items").EnumerateArray())));
            var statusOnly = await QueryAsync(client, "status=2");
            Assert.Equal([false, true, false], Markers(Assert.Single(statusOnly.GetProperty("items").EnumerateArray())));
            var noNumberOrStatus = await QueryAsync(client, "shopSid=30&platform=TemuPop");
            Assert.Equal([false, false, false],
                Markers(noNumberOrStatus.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == Id("a"))));
        });
    }

    [Fact]
    public async Task Filters_inclusive_creation_dates_and_paginates_stably_with_unknown_times_last()
    {
        // Given 分别位于起止边界上的订单、同一创建时间的两张订单、无时间和无效时间。
        await SaveAsync(Samples());
        await VerifyAsync(async client =>
        {
            // When 按来源日期包含两端查询；Then 每页只计主单，并按日期倒序及稳定键分页。
            const string dates = "createdFrom=2025-08-12&createdTo=2025-08-13&pageSize=1";
            var first = await QueryAsync(client, dates);
            var second = await QueryAsync(client, $"{dates}&page=2");
            var third = await QueryAsync(client, $"{dates}&page=3");
            Assert.Equal(3, first.GetProperty("total").GetInt32());
            Assert.Equal(3, second.GetProperty("total").GetInt32());
            Assert.Equal(3, third.GetProperty("total").GetInt32());
            Assert.Equal(1, first.GetProperty("pageSize").GetInt32());
            Assert.Equal(2, second.GetProperty("page").GetInt32());
            Assert.Equal([Id("b"), Id("c"), Id("a")], Ids(first).Concat(Ids(second)).Concat(Ids(third)));
            Assert.Equal([Id("b"), Id("c")], Ids(await QueryAsync(client, "createdFrom=2025-08-13")));
            Assert.Equal([Id("a")], Ids(await QueryAsync(client, "createdTo=2025-08-12")));
            var beyond = await QueryAsync(client, $"{dates}&page=99");
            Assert.Equal(3, beyond.GetProperty("page").GetInt32());
            Assert.Equal([Id("a")], Ids(beyond));

            // When 不限日期；Then 缺失和无效值均排序在已知日期之后，界面收到 null。
            var all = await QueryAsync(client);
            Assert.Equal(20, all.GetProperty("pageSize").GetInt32());
            Assert.Equal([Id("b"), Id("c"), Id("a"), Id("d"), Id("e")], Ids(all));
            var items = all.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal("2025-08-13T23:59:59+08:00", items[0].GetProperty("created").GetString());
            Assert.Equal("2025-08-12T00:00", items[2].GetProperty("created").GetString());
            Assert.Equal(JsonValueKind.Null, items[3].GetProperty("created").ValueKind);
            Assert.Equal(JsonValueKind.Null, items[3].GetProperty("dispatched").ValueKind);
            Assert.Equal("2023-11-14T22:13:20Z", items[4].GetProperty("dispatched").GetString());
            Assert.Equal(JsonValueKind.Null, items[4].GetProperty("created").ValueKind);
            Assert.Equal(0, items[4].GetProperty("lineCount").GetInt32());
            Assert.Equal(0, items[4].GetProperty("itemQuantity").GetInt64());
        });
    }

    [Fact]
    public async Task Reads_source_date_formats_only_when_the_date_can_be_determined()
    {
        // Given 来源可使用不同的日月顺序和时间精度，部分日月值无法确定顺序。
        var template = Samples()[0];
        OrderSnapshot WithTimes(string id, string created, string? dispatched = null) => template with
        {
            ErpId = Id(id),
            Times = new(new(created, "list"), null, dispatched is null ? null : new(dispatched, "detail"), null)
        };
        await SaveAsync([
            WithTimes("a", "13-08-2025 05", "08-13-2025 06:21:44"),
            WithTimes("b", "08-13-2025 04:30", "07-07-2025"),
            WithTimes("c", "08-08-2025 03:04:05", "08-09-2025 04:05"),
            WithTimes("d", "08-09-2025"),
            WithTimes("e", "31-02-2025"),
            WithTimes("f", "2025-08-13 03"),
            WithTimes("g", "2025-08-11")
        ]);

        await VerifyAsync(async client =>
        {
            // When 限定日期或查看全部；Then 使用唯一的来源日期，歧义和无效日历日期排在末尾。
            var selected = await QueryAsync(client, "createdFrom=2025-08-13&createdTo=2025-08-13");
            Assert.Equal([Id("a"), Id("b"), Id("f")], Ids(selected));
            var all = await QueryAsync(client);
            var rows = all.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal([Id("a"), Id("b"), Id("f"), Id("g"), Id("c"), Id("d"), Id("e")], Ids(all));
            Assert.Equal(["2025-08-13T05", "2025-08-13T04:30", "2025-08-13T03",
                    "2025-08-11", "2025-08-08T03:04:05", null, null],
                rows.Select(row => row.GetProperty("created").GetString()));
            Assert.Equal("2025-08-13T06:21:44", rows[0].GetProperty("dispatched").GetString());
            Assert.Equal("2025-07-07", rows[1].GetProperty("dispatched").GetString());
            Assert.Equal(JsonValueKind.Null, rows[4].GetProperty("dispatched").ValueKind);
        });
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("createdFrom=2025-8-12")]
    [InlineData("createdTo=2025-02-30")]
    [InlineData("createdFrom=2025-08-13&createdTo=2025-08-12")]
    public async Task Rejects_invalid_pagination_and_creation_dates(string query)
    {
        // Given 已登录；When 提交无效页码或日期；Then 返回明确的 400。
        await VerifyAsync(async client =>
        {
            using var response = await client.GetAsync($"/api/orders?{query}", Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        });
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);

    private async Task SaveAsync(IReadOnlyList<OrderSnapshot> orders)
    {
        await using var db = CreateContext();
        var result = await new OrderStore(db).SaveAsync(orders, Ct);
        Assert.False(result.IsErr);
    }

    private async Task VerifyAsync(Func<HttpClient, Task> verify, bool authenticate = true)
    {
        // 运行实际 API 组合根。所用测试环境只启用账户及业务读取，与对象存储无关。
        var result = await AuthenticationStartupTests.RunApiOnDatabaseAsync(_connection, null, [], Ct,
            configureStart: start =>
            {
                start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
                start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
                start.Environment["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
            },
            verifyListening: async (endpoint, ct) =>
            {
                using var client = new HttpClient(new HttpClientHandler { UseCookies = false, UseProxy = false })
                {
                    BaseAddress = endpoint
                };
                client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
                await AuthenticationIntegrationTests.RefreshCsrfAsync(client, ct);
                using var initialized = await client.PostAsJsonAsync("/api/auth/initialize", new { newPassword = fixture.AdminPassword }, ct);
                Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
                if (authenticate)
                    await AuthenticationIntegrationTests.LoginAsync(client, ct, fixture.AdminPassword);

                await verify(client);
            });
        Assert.True(result.Started, "测试 API 未启动。");
    }

    private static async Task<JsonElement> QueryAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"/api/orders?{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string Id(string suffix) => OrderPrefix + suffix;

    private static IEnumerable<string?> TextValues(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString());

    private static IEnumerable<string?> Ids(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString());

    private static IEnumerable<bool> Markers(JsonElement order) =>
        order.GetProperty("platformOrders").EnumerateArray().Select(po => po.GetProperty("matched").GetBoolean());

    private static OrderSnapshot[] Samples() =>
    [
        new(Id("a"), "erp-main-a", 30, "TemuPop", "已取消", new(new("2025-08-12 00:00", "list"), null,
            new("2025-08-13 08:30:12", "detail"), null),
            [new("PO-%_\\A", 30, "4", "已发货"), new("PO-2", 30, "2", null), new("PO-3", 30, null, " ")],
            [new(null, null, null, null, 3, true, false), new(null, null, null, null, 4, false, true)]),
        new(Id("b"), "erp-main-b", 31, "TemuPop", "正常", new(new("2025-08-13T23:59:59+08:00", "detail"), null, null, null),
            [new("PO-%_\\A", 31, "4", "已发货")], []),
        new(Id("c"), "erp-main-c", 30, "OtherPlatform", "正常", new(new("2025-08-13T23:59:59+08:00", "detail"), null, null, null),
            [new("PO-%_\\A", 30, "4", "已发货")], []),
        new(Id("d"), "erp-main-d", 30, "TemuPop", "正常", new(new("2025-02-30 09:00", "detail"), null,
            new("unknown", "detail"), null),
            [new("PO-5", 30, "3", "待处理"), new("PO-xx\\A", 30, "4", "已发货")], []),
        new(Id("e"), "erp-main-e", 30, "TemuPop", "正常", new(null, null, new("1700000000", "dispatchTime"), null), [], [])
    ];
}
