using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenERP.Api.Erp;
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
    public async Task Lists_current_shop_names_and_keeps_history_for_missing_shops()
    {
        // Given 两家店铺有历史订单，只有一家仍是已录入店铺，另有一家店尚无订单。
        await SaveAsync(Samples());
        await using var db = CreateContext();
        // 直接写入授权店铺目录，固定店铺编号。
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Shops" ("Id", "Sid", "Name", "Platform")
            VALUES (uuidv7(), 30, '目录中的店', 'TemuPop'),
                   (uuidv7(), 35, '尚无订单', 'TemuPop')
            """, Ct);
        await VerifyAsync(async client =>
        {
            // When 读取本地列表；Then 筛选项和表格都显示当前名称，历史店保留 SID。
            var page = await QueryAsync(client);
            var shops = page.GetProperty("options").GetProperty("shops").EnumerateArray()
                .Select(shop => (shop.GetProperty("sid").GetInt32(), shop.GetProperty("name").GetString()));
            Assert.Equal([(30, "目录中的店"), (31, (string?)null)], shops);
            Assert.All(page.GetProperty("items").EnumerateArray().Where(row => row.GetProperty("sid").GetInt32() == 30),
                row => Assert.Equal("目录中的店", row.GetProperty("shopName").GetString()));
            var history = await QueryAsync(client, "shopSid=31");
            var row = Assert.Single(history.GetProperty("items").EnumerateArray());
            Assert.Equal(Id("b"), row.GetProperty("id").GetString());
            Assert.Null(row.GetProperty("shopName").GetString());
        });
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

    [Fact]
    public async Task Reports_orders_by_day_shop_platform_status_logistics_and_product()
    {
        // Given 三张订单分属两家店铺、两个平台，金额币种不同，其中一张已付款并出库。
        await SaveAsync(
        [
            new(Id("r1"), "erp-r1", 30, "TemuPop", "正常",
                new(new("2025-08-12 10:00", "detail"), new("2025-08-12 10:30", "detail"), new("2025-08-12 18:00", "detail"), null),
                [], [new("p1", null, null, "SKU-1", 3, false, false, "商品一"), new("p2", null, null, "SKU-2", 2, false, false, "商品二")],
                TotalAmount: 10m, TotalCurrency: "USD", Logistics: "DHL"),
            new(Id("r2"), "erp-r2", 30, "TemuPop", "已取消", new(new("2025-08-13 09:00", "detail"), null, null, null),
                [], [new("p1", null, null, "SKU-1", 1, false, false, "商品一")],
                TotalAmount: 20m, TotalCurrency: "USD", Logistics: "DHL"),
            new(Id("r3"), "erp-r3", 31, "OtherPlatform", "正常", new(new("2025-08-13 11:00", "detail"), null, null, null),
                [], [], TotalAmount: 5m, TotalCurrency: "EUR")
        ]);
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Shops" ("Id", "Sid", "Name", "Platform") VALUES (uuidv7(), 30, '目录中的店', 'TemuPop')
            """, Ct);
        await VerifyAsync(async client =>
        {
            // When 读取报表；Then 汇总、按日、按店铺、平台、状态、物流和商品的口径都来自本地订单。
            var report = await ReportAsync(client);
            var summary = report.GetProperty("summary");
            Assert.Equal(3, summary.GetProperty("orders").GetInt32());
            Assert.Equal(6, summary.GetProperty("itemQuantity").GetInt64());
            Assert.Equal(1, summary.GetProperty("dispatched").GetInt32());
            Assert.Equal(1, summary.GetProperty("paid").GetInt32());
            Assert.Equal([("EUR", 5m), ("USD", 30m)], Amounts(summary.GetProperty("amounts")));
            Assert.Equal([("2025-08-12", 1), ("2025-08-13", 2)], Buckets(report, "daily"));
            Assert.Equal([("30", 2), ("31", 1)], Buckets(report, "shops"));
            Assert.Equal("目录中的店", report.GetProperty("shops").EnumerateArray().First().GetProperty("label").GetString());
            Assert.Equal([("OtherPlatform", 1), ("TemuPop", 2)], Buckets(report, "platforms"));
            Assert.Equal([("已取消", 1), ("正常", 2)], Buckets(report, "statuses"));
            Assert.Equal([("DHL", 2), ("未填写", 1)], Buckets(report, "logistics"));
            var products = report.GetProperty("products").EnumerateArray().ToArray();
            Assert.Equal(["p1", "p2"], products.Select(p => p.GetProperty("productId").GetString()));
            Assert.Equal([4L, 2L], products.Select(p => p.GetProperty("quantity").GetInt64()));

            // When 按平台和创建日期筛选；Then 汇总只统计命中的订单。
            var filtered = await ReportAsync(client, "platform=TemuPop&createdFrom=2025-08-13&createdTo=2025-08-13");
            Assert.Equal(1, filtered.GetProperty("summary").GetProperty("orders").GetInt32());
        });
    }

    [Fact]
    public async Task Rejects_an_invalid_report_range_and_anonymous_access()
    {
        await VerifyAsync(async client =>
        {
            // Given 未登录；When 读取报表；Then 返回 401。
            using var anonymous = await client.GetAsync("/api/orders/report", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            // Given 已登录；When 起始日期晚于结束日期；Then 返回 400。
            await AuthenticationIntegrationTests.LoginAsync(client, Ct, fixture.AdminPassword);
            using var invalid = await client.GetAsync("/api/orders/report?createdFrom=2025-08-14&createdTo=2025-08-13", Ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }, authenticate: false);
    }

    [Fact]
    public async Task Print_labels_rejects_anonymous_invalid_and_unknown_orders()
    {
        await SaveAsync(Samples());
        await VerifyAsync(async client =>
        {
            // Given 未登录；When 请求打印；Then 返回 401。
            using var anonymous = await client.PostAsJsonAsync("/api/orders/print-labels", new { ids = new[] { Id("a") } }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            // Given 已登录；When 订单数为 0、超过 100 或订单不存在；Then 分别返回 400、400、404。
            await AuthenticationIntegrationTests.LoginAsync(client, Ct, fixture.AdminPassword);
            using var empty = await client.PostAsJsonAsync("/api/orders/print-labels", new { ids = Array.Empty<string>() }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            using var tooMany = await client.PostAsJsonAsync("/api/orders/print-labels",
                new { ids = Enumerable.Range(0, 101).Select(i => Id($"x{i}")).ToArray() }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
            using var unknown = await client.PostAsJsonAsync("/api/orders/print-labels", new { ids = new[] { Id("nope") } }, Ct);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }, authenticate: false);
    }

    [Fact]
    public async Task Print_labels_reports_each_platform_order_that_cannot_be_printed()
    {
        // Given 一张平台没有打印实现的订单，一张 AliExpress 订单（店铺已录入但未配置 ERP AccessToken）。
        await SaveAsync([
            Samples()[2],
            new(Id("ali"), "erp-ali", 40, "AliExpress", "正常", new(null, null, null, null),
                [new("ALI-1", 40, "1", "待发货")], [])
        ]);
        await using (var db = CreateContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Shops" ("Id", "Sid", "Name", "Platform") VALUES (uuidv7(), 40, 'Ali店', 'AliExpress')
                """, Ct);
        await VerifyAsync(async client =>
        {
            // When 勾选两张订单请求面单；Then 不返回任何文档，每个平台单号都带有未取得的原因。
            using var response = await client.PostAsJsonAsync("/api/orders/print-labels",
                new { ids = new[] { Id("c"), Id("ali") } }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Empty(body.GetProperty("documents").EnumerateArray());
            var failures = body.GetProperty("failures").EnumerateArray()
                .ToDictionary(item => item.GetProperty("platformOrderNumber").GetString()!,
                    item => item.GetProperty("reason").GetString()!);
            Assert.Contains("暂不支持", failures["PO-%_\\A"]);
            Assert.False(string.IsNullOrWhiteSpace(failures["ALI-1"]));
        });
    }

    private static async Task<JsonElement> ReportAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"/api/orders/report?{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static IEnumerable<(string?, int)> Buckets(JsonElement report, string name) =>
        report.GetProperty(name).EnumerateArray()
            .Select(bucket => (bucket.GetProperty("key").GetString(), bucket.GetProperty("orders").GetInt32()))
            .OrderBy(bucket => bucket.Item1, StringComparer.Ordinal);

    private static IEnumerable<(string?, decimal)> Amounts(JsonElement amounts) =>
        amounts.EnumerateArray()
            .Select(amount => (amount.GetProperty("currency").GetString(), amount.GetProperty("total").GetDecimal()))
            .OrderBy(amount => amount.Item1, StringComparer.Ordinal);

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseOpenErpPostgres(_connection).Options);

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
