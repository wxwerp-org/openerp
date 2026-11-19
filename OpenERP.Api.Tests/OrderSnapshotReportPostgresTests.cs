using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Npgsql;
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
        // Given 查询开始时共16件，在数量聚合完成后另一连接更新其中一行。
        await SeedAsync();
        var changed = false;
        var interceptor = new AfterQuantityRead(async ct =>
        {
            await using var writer = Context();
            await writer.Database.ExecuteSqlRawAsync(
                "UPDATE \"OrderLines\" SET \"Quantity\" = 8 WHERE \"ErpId\" = 'a' AND \"Ordinal\" = 0", ct);
            changed = true;
        });
        await using var reader = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connection, options => options.EnableRetryOnFailure()).AddInterceptors(interceptor).Options);

        // When 明细在并发更新提交之后读取；Then 当前报表仍是16件，下一次查询才反映21件。
        var result = await new OrderSnapshotReportQuery(reader, new FixedClock()).QueryAsync(new(null, []), TestContext.Current.CancellationToken);
        if (result.IsErr) throw new XunitException(result.Error.Message);
        Assert.True(changed, "必须实际触发并发更新，才能证明查询一致性。");
        Assert.Equal(16, result.Value.Summary.ItemQuantity);
        Assert.Equal(16, result.Value.Orders.Sum(order => order.ItemQuantity));
        Assert.Equal(5, result.Value.Orders.Single(order => order.Id == "a").ItemQuantity);
        Assert.Equal(21, (await QueryAsync(new(null, []))).Summary.ItemQuantity);
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
