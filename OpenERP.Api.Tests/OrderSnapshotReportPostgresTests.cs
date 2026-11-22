using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Security.Claims;
using Npgsql;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Reports;
using OpenERP.Core.Orders;
using OpenERP.Core.Reports;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderSnapshotReportPostgresTests(AspireIntegrationFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    public static bool LiveConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENERP_TEST_ERP_TOKEN"));
    private readonly string _databaseName = $"openerp_report_test_{Guid.NewGuid():N}";
    private string _admin = "";
    private string _connection = "";
    private bool _created;
    private static readonly DateTime QueryTime = new(2026, 11, 17, 2, 3, 4, DateTimeKind.Utc);

    [Fact(Skip = "未配置真实 ERP 验收凭证。", SkipUnless = nameof(LiveConfigured))]
    public async Task Real_source_orders_match_report_totals_and_every_paged_detail()
    {
        // Given 真实店铺的只读来源快照和本测试专用数据库；不写上游或开发数据库。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(8));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        var token = new ErpAccessToken(Environment.GetEnvironmentVariable("OPENERP_TEST_ERP_TOKEN")!.Trim());
        var read = await new WxwErpClient(http).ReadOrdersAsync(30, null, token, timeout.Token);
        if (read.IsErr) throw new XunitException(read.Error.Message);
        var source = read.Value.Orders;
        Assert.NotEmpty(source);
        await SaveAsync(source);

        // When 重放同步并逐页读报表；Then 与源端每单/每行原值一致，没有重复或遗漏。
        await SaveAsync(source);
        var first = await QueryAsync(new(null, [], 1, 100));
        Assert.Equal(source.Count, first.Summary.OrderCount);
        Assert.Equal(source.Sum(order => order.Lines.Sum(line => (long)line.Quantity)), first.Summary.ItemQuantity);
        var orders = first.Orders.ToList();
        for (var page = 2; page <= first.PageCount; page++)
            orders.AddRange((await QueryAsync(new(null, [], page, 100))).Orders);
        Assert.Equal(source.Select(order => order.ErpId).Order(), orders.Select(order => order.Id).Order());
        foreach (var expected in source)
        {
            var actual = orders.Single(order => order.Id == expected.ErpId);
            Assert.Equal(expected.OrderNumber, actual.Number);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Platform, actual.Shop.Platform);
            Assert.Equal(expected.Sid.ToString(System.Globalization.CultureInfo.InvariantCulture), actual.Shop.ShopId);
            Assert.Equal(expected.PlatformOrders.Select(po => po.PoNumber).Order(), actual.PlatformOrders.Select(po => po.Number).Order());
            Assert.Equal(expected.Lines.Select(line => (line.Quantity, line.Disabled, line.Appended)),
                actual.Items.Select(line => (line.Quantity, line.Disabled, line.Appended)));
        }
        output.WriteLine($"真实源端 {read.Value.Total} 张主单，本次接受 {source.Count} 张；报表 {first.Summary.OrderCount} 张、{first.Summary.ItemQuantity} 件，逐单逐行核对一致；源读取跳过 {read.Value.SkipReasons.Values.Sum()} 张。");
    }

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _admin = await fixture.GetPostgresConnectionStringAsync(ct);
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _databaseName, Pooling = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"""CREATE DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync(ct);
        _created = true;
        await using var db = Context();
        await db.Database.MigrateAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_created) return;
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""DROP DATABASE "{_databaseName}" """, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Groups_source_orders_without_join_multiplication_or_status_deductions()
    {
        // Given 两个平台、三个店铺身份、重复显示单号、零数量和退款状态，首单关联两个平台单。
        await SeedAsync();

        // When 查询本地快照；Then 所有来源单只算一次，原值之和16，状态和标志不改写件数。
        var report = await QueryAsync(new(null, []));
        Assert.Equal(new OrderSnapshotTotals(4, 16), report.Summary);
        Assert.Equal(3, report.Groups.Count);
        Assert.Equal(new OrderSnapshotTotals(2, 9), Totals(report.Groups.Single(group => group.Shop.Key == ShopKey("TemuPop", "30"))));
        Assert.Equal(new OrderSnapshotTotals(1, 0), Totals(report.Groups.Single(group => group.Shop.ShopId == "31")));
        Assert.Equal(new OrderSnapshotTotals(1, 7), Totals(report.Groups.Single(group => group.Shop.Platform == "OtherPlatform")));
        Assert.Equal(["OtherPlatform", "TemuPop"], report.Platforms);
        Assert.Equal(3, report.Shops.Count);
        Assert.Equal(QueryTime, report.GeneratedAtUtc);
        Assert.Equal(DateTimeKind.Utc, report.GeneratedAtUtc.Kind);
        var first = report.Orders.Single(order => order.Id == "a");
        Assert.Equal(2, first.PlatformOrders.Count);
        Assert.Equal(5, first.ItemQuantity);
        Assert.True(first.Items[0].Disabled);
        Assert.True(first.Items[1].Appended);
        Assert.Equal("退款", report.Orders.Single(order => order.Id == "d").Status);
        Assert.Equal(0, report.Orders.Single(order => order.Id == "b").ItemQuantity);
    }

    [Fact]
    public async Task Filters_platform_and_complete_shop_identity_without_broadening_unknown_selections()
    {
        // Given 两个平台共享同一店铺编号。
        await SeedAsync();

        // When 选平台、选一家店、跨平台选店或选不存在的店；Then 过滤与汇总准确且未知键不退回全部。
        Assert.Equal(new OrderSnapshotTotals(3, 9), (await QueryAsync(new("TemuPop", []))).Summary);
        Assert.Equal(new OrderSnapshotTotals(2, 9), (await QueryAsync(new(null, [ShopKey("TemuPop", "30")]))).Summary);
        Assert.Equal(new OrderSnapshotTotals(3, 16), (await QueryAsync(new(null,
            [ShopKey("TemuPop", "30"), ShopKey("OtherPlatform", "30")]))).Summary);
        var missing = await QueryAsync(new(null, ["another-source/TemuPop/30"]));
        Assert.Equal(new OrderSnapshotTotals(0, 0), missing.Summary);
        Assert.Empty(missing.Orders);
        Assert.Equal(3, missing.Shops.Count);
        Assert.Empty((await QueryAsync(new("TemuPop", [ShopKey("OtherPlatform", "30")]))).Groups);
    }

    [Fact]
    public async Task Pagination_keeps_full_totals_and_covers_each_order_once()
    {
        // Given 四张来源订单；When 逐页查询；Then 每页明细不同，汇总保持全量且越界页落到最后页。
        await SeedAsync();
        var ids = new List<string>();
        long quantity = 0;
        for (var page = 1; page <= 4; page++)
        {
            var report = await QueryAsync(new(null, [], page, 1));
            Assert.Equal(new OrderSnapshotTotals(4, 16), report.Summary);
            Assert.Equal(4, report.PageCount);
            Assert.Equal(page, report.Page);
            var order = Assert.Single(report.Orders);
            ids.Add(order.Id);
            quantity += order.ItemQuantity;
        }
        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equal(16, quantity);
        var beyond = await QueryAsync(new(null, [], 99, 1));
        Assert.Equal(4, beyond.Page);
        Assert.Equal(ids[^1], Assert.Single(beyond.Orders).Id);
    }

    [Fact]
    public async Task Repeated_sync_does_not_inflate_totals_and_updates_replace_current_quantities()
    {
        // Given 已保存的订单再次同步。
        var orders = Samples();
        await SaveAsync(orders);
        await SaveAsync(orders);
        Assert.Equal(new OrderSnapshotTotals(4, 16), (await QueryAsync(new(null, []))).Summary);

        // When 源端将第一行数量从3改成1；Then 数量减少2，未返回的其它来源单仍按既有同步边界保留。
        var changed = orders[0] with { Lines = [orders[0].Lines[0] with { Quantity = 1 }, orders[0].Lines[1]] };
        await SaveAsync([changed]);
        Assert.Equal(new OrderSnapshotTotals(4, 14), (await QueryAsync(new(null, []))).Summary);
    }

    [Fact]
    public async Task Concurrent_sync_between_totals_and_details_does_not_mix_snapshots()
    {
        // Given 查询开始时共16件；聚合期间由另一连接更新当前日订单金额和件数。
        var orders = Samples();
        orders[0] = orders[0] with
        {
            Times = new(new("2026-11-17 13:00", "list"), null, null, null),
            IncomeCost = 5m,
            IncomeCurrency = "USD"
        };
        await SaveAsync(orders);
        var changed = false;
        var interceptor = new AfterQuantityRead(async ct =>
        {
            await using var writer = Context();
            await writer.Database.ExecuteSqlRawAsync(
                """
                UPDATE "OrderLines" SET "Quantity" = 8 WHERE "ErpId" = 'a' AND "Ordinal" = 0;
                UPDATE "Orders" SET "IncomeCost" = 10 WHERE "ErpId" = 'a';
                """, ct);
            changed = true;
        });
        await using var reader = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connection, options => options.EnableRetryOnFailure()).AddInterceptors(interceptor).Options);

        // When 并发更新后读取明细、销售额与走势；Then 当前报表保持旧快照，下次查询才反映新值。
        var result = await new OrderSnapshotReportQuery(reader, new FixedClock()).QueryAsync(new(null, []), TestContext.Current.CancellationToken);
        if (result.IsErr) throw new XunitException(result.Error.Message);
        Assert.True(changed, "必须实际触发并发更新，才能证明查询一致性。");
        Assert.Equal(16, result.Value.Summary.ItemQuantity);
        Assert.Equal(16, result.Value.Orders.Sum(order => order.ItemQuantity));
        Assert.Equal(5, result.Value.Orders.Single(order => order.Id == "a").ItemQuantity);
        Assert.Equal(5m, result.Value.Sales.Amount);
        Assert.Equal(new OrderTrendPoint("2026-11-17", 1, 5, 5m), result.Value.Trend.Points[^1]);
        var next = await QueryAsync(new(null, []));
        Assert.Equal(21, next.Summary.ItemQuantity);
        Assert.Equal(10m, next.Sales.Amount);
        Assert.Equal(new OrderTrendPoint("2026-11-17", 1, 10, 10m), next.Trend.Points[^1]);
    }

    [Fact]
    public async Task Empty_database_returns_an_explicit_empty_report()
    {
        // Given 空的已迁移数据库；When 查询；Then 没有虚构店铺/订单，数量为零且分页可用。
        var report = await QueryAsync(new(null, [], 99, 20));
        Assert.Equal(new OrderSnapshotTotals(0, 0), report.Summary);
        Assert.Empty(report.Orders);
        Assert.Empty(report.Groups);
        Assert.Empty(report.Shops);
        Assert.Empty(report.Platforms);
        Assert.Equal(1, report.Page);
        Assert.Equal(1, report.PageCount);
    }

    [Fact]
    public async Task Missing_order_storage_is_reported_as_a_failure()
    {
        // Given 测试数据库的订单存储不可用；When 查询；Then 返回错误，不冒充无订单。
        await using var db = Context();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"OrderLines\"", TestContext.Current.CancellationToken);
        var result = await new OrderSnapshotReportQuery(db, new FixedClock()).QueryAsync(new(null, []), TestContext.Current.CancellationToken);
        if (!result.IsErr) throw new XunitException("存储不可用时必须返回读取失败。");
        Assert.Equal("读取本地订单失败，请重试。", result.Error.Message);
    }

    [Fact]
    public async Task Trend_uses_wall_clock_dates_and_includes_both_window_boundaries()
    {
        // Given 两个窗口的起止边界、日期缺口和无效时间，含 ISO、年在后和 epoch。
        await SaveAsync([
            TrendOrder("previous-start", "2026-11-04 00:00", 2, 2m),
            TrendOrder("previous-end", "2026-11-10 23", 3, null),
            TrendOrder("start", "2026-11-11T00:10:00+08:00", 4, 10m),
            TrendOrder("partial", "2026-11-11 13:00", 5, null),
            TrendOrder("unknown", "15-11-2026 22:12", 6, null),
            TrendOrder("end", new DateTimeOffset(2026, 11, 17, 23, 59, 0, TimeSpan.Zero)
                .ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), 7, 0m),
            TrendOrder("too-old", "2026-11-03 23:59", 50, 100m),
            TrendOrder("future", "2026-11-18 00:00", 50, 200m),
            TrendOrder("bad-calendar", "2026-11-31 12:00", 9, 8m),
            TrendOrder("missing", null, 10, 4m)
        ]);

        // When 省略起止日期查询 7 天走势，并用订单列表查询同一日期窗口。
        var report = await QueryAsync(new(null, []));
        var trend = report.Trend;
        var end = new DateOnly(2026, 11, 17);
        var start = end.AddDays(-6);
        await using var db = Context();
        var list = await new OrderListQuery(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderListQuery>.Instance)
            .ReadAsync(new(null, null, null, null, start, end, 1, 20), AdministratorAccess(),
                TestContext.Current.CancellationToken);

        // Then 墙钟日期与列表一致，缺日补零，未知金额独立记数。
        if (list.Result.IsErr) throw new XunitException(list.Result.Error.Message);
        var listed = list.Result.Value;
        Assert.Equal(4, listed.Total);
        Assert.Equal(22, listed.Items.Sum(item => item.ItemQuantity));
        Assert.Equal(7, trend.Days);
        Assert.Equal("2026-11-11", trend.Start);
        Assert.Equal(end, trend.End);
        Assert.Equal("USD", trend.Currency);
        Assert.Equal(new OrderTrendPoint[]
        {
            new("2026-11-11", 2, 9, 10m),
            new("2026-11-12", 0, 0, 0m),
            new("2026-11-13", 0, 0, 0m),
            new("2026-11-14", 0, 0, 0m),
            new("2026-11-15", 1, 6, null),
            new("2026-11-16", 0, 0, 0m),
            new("2026-11-17", 1, 7, 0m)
        }, trend.Points);
        foreach (var point in trend.Points)
        {
            var listedOnDate = listed.Items.Where(item => item.Created?.StartsWith(point.Date, StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(point.OrderCount, listedOnDate.LongLength);
            Assert.Equal(point.ItemQuantity, listedOnDate.Sum(item => item.ItemQuantity));
        }
        Assert.Equal(new OrderTrendPrevious(2, 5, 2m), trend.Previous);
        Assert.Equal(2, trend.MissingCreatedCount);
        Assert.Equal(2, trend.AmountKnownOrderCount);
        Assert.Equal(2, trend.AmountUnknownOrderCount);
        Assert.Equal(new OrderSnapshotSales(324m, "USD", 7, 3), report.Sales);
    }

    [Fact]
    public async Task Sales_and_custom_trend_share_platform_and_shop_filters()
    {
        // Given 多平台相同店铺编号、混合币种、无创建日期及已知金额为零的订单。
        await SaveAsync([
            TrendOrder("selected", "2026-10-19 12:00", 4, 10m),
            TrendOrder("selected-missing", null, 1, 5m),
            TrendOrder("other-shop", "2026-11-17 08:00", 2, 11m) with { Sid = 31 },
            TrendOrder("other-platform", "2026-11-17 08:00", 3, 0m, "EUR") with { Platform = "OtherPlatform" }
        ]);

        // When 指定 30 天的区间并组合平台和店铺条件，再查询未筛选及条件无交集时的结果。
        var start = new DateOnly(2026, 10, 19);
        var end = new DateOnly(2026, 11, 17);
        var request = new OrderSnapshotReportRequest("TemuPop",
            [ShopKey("TemuPop", "30"), ShopKey("OtherPlatform", "30")], TrendStart: start, TrendEnd: end);
        var filtered = await QueryAsync(request);
        var all = await QueryAsync(request with { Platform = null, Shops = [] });
        var none = await QueryAsync(request with { Shops = [ShopKey("OtherPlatform", "30")] });

        // Then 总销售额包含无日期订单，走势逐日连续，无更早历史时环比为空，混合币种仍返回金额。
        Assert.Equal(new OrderSnapshotSales(15m, "USD", 2, 0), filtered.Sales);
        Assert.Equal(new OrderSnapshotTotals(2, 5), filtered.Summary);
        Assert.Single(filtered.Groups);
        Assert.Equal(2, filtered.Orders.Count);
        Assert.Equal("USD", filtered.Trend.Currency);
        Assert.Equal(1, filtered.Trend.MissingCreatedCount);
        Assert.Null(filtered.Trend.Previous);
        Assert.Equal(30, filtered.Trend.Days);
        Assert.Equal("2026-10-19", filtered.Trend.Start);
        Assert.Equal(end, filtered.Trend.End);
        Assert.Equal(Enumerable.Range(0, 30).Select(offset => start.AddDays(offset).ToString("yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture)), filtered.Trend.Points.Select(point => point.Date));
        Assert.Equal(new OrderTrendPoint("2026-10-19", 1, 4, 10m), filtered.Trend.Points[0]);
        Assert.Equal(new OrderTrendPoint("2026-11-17", 0, 0, 0m), filtered.Trend.Points[^1]);
        Assert.Equal(new OrderSnapshotSales(26m, null, 4, 0), all.Sales);
        Assert.Null(all.Trend.Currency);
        Assert.Equal(new OrderTrendPoint("2026-11-17", 2, 5, 11m), all.Trend.Points[^1]);
        Assert.Equal(new OrderSnapshotSales(null, null, 0, 0), none.Sales);
        Assert.Null(none.Trend.Previous);
        Assert.Equal(0, none.Trend.MissingCreatedCount);
    }

    [Fact]
    public async Task Leap_year_trend_returns_366_consecutive_days_and_an_equally_long_previous_window()
    {
        // Given 当前和上期窗口边界均有订单，且各有一张订单落在整个区间之前和之后。
        await SaveAsync([
            TrendOrder("too-old", "2022-12-30", 10, 10m),
            TrendOrder("previous-start", "2022-12-31", 2, 2m),
            TrendOrder("previous-end", "2023-12-31", 3, 3m),
            TrendOrder("start", "2024-01-01", 4, 4m),
            TrendOrder("leap-day", "2024-02-29", 5, 5m),
            TrendOrder("end", "2024-12-31", 6, 6m),
            TrendOrder("future", "2025-01-01", 20, 20m)
        ]);

        // When 查询完整的闰年区间。
        var start = new DateOnly(2024, 1, 1);
        var end = new DateOnly(2024, 12, 31);
        var report = await QueryAsync(new(null, [], TrendStart: start, TrendEnd: end));

        // Then 走势按日连续并含闰日，环比覆盖上期的两个边界，区间外订单仍计入总销售额。
        var trend = report.Trend;
        Assert.Equal(366, trend.Days);
        Assert.Equal("2024-01-01", trend.Start);
        Assert.Equal(end, trend.End);
        Assert.Equal(Enumerable.Range(0, 366).Select(offset => start.AddDays(offset).ToString("yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture)), trend.Points.Select(point => point.Date));
        Assert.Equal(new OrderTrendPoint("2024-01-01", 1, 4, 4m), trend.Points[0]);
        Assert.Equal(new OrderTrendPoint("2024-02-28", 0, 0, 0m), trend.Points[58]);
        Assert.Equal(new OrderTrendPoint("2024-02-29", 1, 5, 5m), trend.Points[59]);
        Assert.Equal(new OrderTrendPoint("2024-12-31", 1, 6, 6m), trend.Points[^1]);
        Assert.Equal(new OrderTrendPrevious(2, 5, 5m), trend.Previous);
        Assert.Equal(new OrderSnapshotSales(50m, "USD", 7, 0), report.Sales);
    }

    [Fact]
    public async Task End_near_the_earliest_date_preserves_the_default_seven_day_window()
    {
        // Given 空数据库与 DateOnly 可表示的最早结束日期。
        var end = new DateOnly(1, 1, 1);

        // When 只指定结束日期。
        var report = await QueryAsync(new(null, [], TrendEnd: end));

        // Then 保留向前六天的窗口和 ISO 8601 年份 0000 的日期输出。
        var trend = report.Trend;
        Assert.Equal(7, trend.Days);
        Assert.Equal("0000-12-26", trend.Start);
        Assert.Equal(end, trend.End);
        Assert.Equal(new[] { "0000-12-26", "0000-12-27", "0000-12-28", "0000-12-29", "0000-12-30", "0000-12-31", "0001-01-01" },
            trend.Points.Select(point => point.Date));
        Assert.Null(trend.Previous);
    }

    [Theory]
    [InlineData("2026-11-03", 0, 0, 0)]
    [InlineData("2026-11-10", 1, 2, null)]
    public async Task Previous_window_distinguishes_empty_days_from_unknown_amount(
        string historicalDate, long count, long quantity, int? amount)
    {
        // Given 已有窗口起点之前的历史订单，且这张订单金额未知。
        await SaveAsync([TrendOrder("history", historicalDate, 2, null)]);

        // When 查询当前趋势。
        var report = await QueryAsync(new(null, []));

        // Then 上期有历史可对比，空窗口为零，有订单但金额未知时为 null。
        Assert.Equal(new OrderTrendPrevious(count, quantity, amount), report.Trend.Previous);
        Assert.All(report.Trend.Points, point => Assert.Equal((0L, 0L, (decimal?)0m),
            (point.OrderCount, point.ItemQuantity, point.SalesAmount)));
        Assert.Equal(new OrderSnapshotSales(null, null, 0, 1), report.Sales);
    }

    [Fact]
    public async Task Resync_updates_an_existing_order_with_amount_and_preserves_zero()
    {
        // Given 已经落库且没有金额的存量订单。
        var order = TrendOrder("existing", "2026-11-17 22:12", 2, null, null);
        await SaveAsync([order]);

        // When 重同步时只补入零金额及币种。
        await using var db = Context();
        var result = await new OrderStore(db).SaveAsync([order with { IncomeCost = 0m, IncomeCurrency = "USD" }],
            TestContext.Current.CancellationToken);

        // Then 保存计为更新，零金额与币种随快照落库。
        if (result.IsErr) throw new XunitException(result.Error.Message);
        Assert.Equal(new OrderSaveCounts(0, 1, 0), result.Value);
        Assert.Equal(new OrderSnapshotSales(0m, "USD", 1, 0), (await QueryAsync(new(null, []))).Sales);
    }

    private static ShopAccess AdministratorAccess() =>
        ShopAccess.For(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, UserAccount.AdministratorRole)], "Test")));

    private static OrderSnapshot TrendOrder(string id, string? created, int quantity, decimal? amount, string? currency = "USD") =>
        new(id, id, 30, "TemuPop", "正常", new(created is null ? null : new(created, "list"), null, null, null),
            [new("PO-1", 30, null, null), new("PO-2", 30, null, null)],
            [new(null, null, null, null, quantity, true, true)], amount, currency);

    private Task SeedAsync() => SaveAsync(Samples());

    private async Task SaveAsync(IReadOnlyList<OrderSnapshot> orders)
    {
        await using var db = Context();
        var result = await new OrderStore(db).SaveAsync(orders, TestContext.Current.CancellationToken);
        Assert.False(result.IsErr);
    }

    private async Task<OrderSnapshotReport> QueryAsync(OrderSnapshotReportRequest request)
    {
        await using var db = Context();
        var result = await new OrderSnapshotReportQuery(db, new FixedClock()).QueryAsync(request, TestContext.Current.CancellationToken);
        if (result.IsErr) throw new XunitException(result.Error.Message);
        return result.Value;
    }

    private AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_connection, options => options.EnableRetryOnFailure()).Options);

    private static string ShopKey(string platform, string sid) => new OrderShop("wxw-erp", platform, sid, "").Key;
    private static OrderSnapshotTotals Totals(OrderShopTotals totals) => new(totals.OrderCount, totals.ItemQuantity);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(QueryTime);
    }

    private sealed class AfterQuantityRead(Func<CancellationToken, Task> update) : DbCommandInterceptor
    {
        private bool _updated;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!_updated && command.CommandText.Contains("sum(", StringComparison.OrdinalIgnoreCase))
            {
                _updated = true;
                await update(cancellationToken);
            }
            return result;
        }
    }

    private static OrderSnapshot[] Samples() =>
    [
        new("a", "shared-number", 30, "TemuPop", "已禁用", new(null, null, null, null),
            [new("po-1", 30, "1", null), new("po-2", 30, null, null)],
            [new("p1", "o1", "s1", "sku1", 3, true, false), new("p2", "o2", null, null, 2, false, true)]),
        new("b", "shared-number", 31, "TemuPop", "取消", new(null, null, null, null),
            [new("po-1", 31, null, null)], [new(null, null, null, null, 0, null, null)]),
        new("c", "shared-number", 30, "OtherPlatform", "正常", new(null, null, null, null),
            [new("po-1", 30, null, null)], [new(null, null, null, null, 7, false, false)]),
        new("d", "refund-number", 30, "TemuPop", "退款", new(null, null, null, null),
            [], [new(null, null, null, null, 4, false, false)])
    ];
}
