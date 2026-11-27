using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Persistence;
using OpenERP.Api.Reports;
using OpenERP.Core.Orders;
using OpenERP.Core.Reports;
using OpResult;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

public sealed class PortableReadQueryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private static readonly DateOnly TrendEnd = new(2026, 11, 17);

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await _connection.OpenAsync(ct);
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(ct);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    [Fact]
    public async Task Sqlite_order_list_preserves_wall_clock_paging_and_matches_a_single_platform_order()
    {
        // Given 两张订单的创建墙钟时间相同，一张日期存在歧义；平台单号含通配符字面值。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await SaveAsync(db, ct, [
            Sample("b", "2026-11-17T00:05:01.123456+08:00", 0.02m),
            Sample("a", "17-11-2026 00:05:01.123456", 0.01m),
            Sample("c", "08-09-2026", null)
        ]);
        var query = new OrderListQuery(db, NullLogger<OrderListQuery>.Instance);
        var access = ShopAccess.For(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, UserAccount.AdministratorRole)], "Test")));

        // When 按创建日期、子单号和状态查询；Then 两张满足条件的主单顺序稳定，越界页被钳制。
        var filter = new OrderListRequest(30, "TemuPop", "已发货", "%_", TrendEnd, TrendEnd, 1, 1);
        var first = RequireValue((await query.ReadAsync(filter, access, ct)).Result);
        Assert.Equal(2, first.Total);
        var item = Assert.Single(first.Items);
        Assert.Equal("a", item.Id);
        Assert.Equal("2026-11-17T00:05:01.123456", item.Created);
        Assert.Equal("2023-11-14T22:13:20Z", item.Dispatched);
        Assert.Equal(2, item.ItemQuantity);
        Assert.Collection(item.PlatformOrders,
            po => Assert.Equal(new PlatformOrderListItem("PO%_1", "已发货", true), po),
            po => Assert.Equal(new PlatformOrderListItem("PO-2", "已发货", false), po));
        var last = RequireValue((await query.ReadAsync(filter with { Page = 99 }, access, ct)).Result);
        Assert.Equal(2, last.Page);
        Assert.Equal("2026-11-17T00:05:01.123456+08:00", Assert.Single(last.Items).Created);
        var all = RequireValue((await query.ReadAsync(filter with { CreatedFrom = null, CreatedTo = null, PageSize = 3 }, access, ct)).Result);
        Assert.Equal(["a", "b", "c"], all.Items.Select(order => order.Id));
        Assert.Null(all.Items[^1].Created);
    }

    [Fact]
    public async Task Sqlite_report_preserves_exact_decimal_totals_and_counts_each_source_order_once()
    {
        // Given 已分配平台有三张主单，另一平台有相同 SID 的店铺。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await SaveAsync(db, ct, [
            Sample("a", "17-11-2026 00", 0.01m),
            Sample("b", "2026-11-17T00:00:00+08:00", 0.02m),
            Sample("c", "2026-11-31", null),
            Sample("other", "2026-11-17", 0.99m) with { Platform = "Amazon" }
        ]);

        // When 按店铺读取报表；Then 同号店铺隔离，三张主单各计一次，无法确定的创建日期不进入按日统计。
        var access = ShopAccess.For(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, UserAccount.AdministratorRole)], "Test")));
        var report = await new OrderReportQuery(db).ReadAsync(new(30, "TemuPop", null, null), access, ct);
        Assert.NotNull(report);
        Assert.Equal(3, report.Summary.Orders);
        Assert.Equal(6, report.Summary.ItemQuantity);
        var day = Assert.Single(report.Daily);
        Assert.Equal("2026-11-17", day.Key);
        Assert.Equal(2, day.Orders);
        Assert.Equal(4, day.ItemQuantity);
    }

    [Fact]
    public async Task Sqlite_shop_search_matches_unicode_case_and_literal_wildcards()
    {
        // Given 两家店名仅在通配符位置不同，其中一家包含非 ASCII 大写字母。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        db.Shops.AddRange(
            new Shop { Sid = 30, Name = "Équipe %_", Platform = "TemuPop", IsTokenOk = true },
            new Shop { Sid = 31, Name = "Équipe XY", Platform = "TemuPop", IsTokenOk = false });
        await db.SaveChangesAsync(ct);

        // When 管理员以小写关键词查询；Then 仅命中字面值匹配的店铺，汇总同步受关键词限制。
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        ErpEndpointTestAuth.Configure(builder);
        await using var app = builder.Build();
        app.MapShopDirectory();
        await app.StartAsync(ct);
        using var http = ErpEndpointTestAuth.CreateClient(app, UserAccount.AdministratorRole);

        using var response = await http.GetAsync("/api/erp/shops?keyword=" + Uri.EscapeDataString("équipe %_"), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ShopDirectoryPage>(ct);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.Summary.Valid);
        Assert.Equal(0, page.Summary.Invalid);
        Assert.Equal("30", Assert.Single(page.Items).Sid);
    }

    [Fact]
    public async Task Sqlite_backfill_resumes_after_a_committed_batch_and_leaves_source_values_intact()
    {
        // Given 超过一批的旧订单和一家旧店铺，创建时间最后一张无法解析。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        db.Orders.AddRange(Enumerable.Range(0, 501).Select(index => new OrderRecord
        {
            ErpId = $"legacy-{index:D3}", OrderNumber = "legacy", Sid = 30, Platform = "TemuPop", Status = "正常",
            CreatedRaw = index == 500 ? "08-09-2026" : "2026-11-17 12", CreatedAtTicks = null,
            SyncRevision = Guid.NewGuid()
        }));
        db.Shops.Add(new Shop { Sid = 30, Name = "Équipe", Platform = "TemuPop", SearchName = null });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // When 首批提交后取消，再用新会话继续；Then 剩余订单处理完成，旧原值及歧义状态保留。
        using var interruption = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using (var interrupted = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).AddInterceptors(new CancelAfterCommit(interruption)).Options))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadModelBackfill.RunAsync(interrupted, interruption.Token));
        }
        Assert.Equal("legacy-500", await db.Orders.Where(order => order.CreatedAtTicks == null).Select(order => order.ErpId).SingleAsync(ct));

        await ReadModelBackfill.RunAsync(db, ct);
        Assert.False(await db.Orders.AnyAsync(order => order.CreatedAtTicks == null, ct));
        Assert.Equal(500, await db.Orders.CountAsync(order => order.CreatedAtTicks >= 0, ct));
        var ambiguous = await db.Orders.SingleAsync(order => order.ErpId == "legacy-500", ct);
        Assert.Equal("08-09-2026", ambiguous.CreatedRaw);
        Assert.Null(OrderSourceTime.Format(ambiguous.CreatedRaw, ambiguous.CreatedAtTicks));
        Assert.Equal("ÉQUIPE", await db.Shops.Select(shop => shop.SearchName).SingleAsync(ct));
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static T RequireValue<T>(OpResult<T> result) where T : notnull
    {
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        return result.Value!;
    }

    private static async Task SaveAsync(AppDbContext db, CancellationToken ct, IReadOnlyList<OrderSnapshot> orders) =>
        Assert.True((await new OrderStore(db).SaveAsync(orders, ct)).IsOk);

    private static OrderSnapshot Sample(string id, string created, decimal? amount) =>
        new(id, id, 30, "TemuPop", "正常", new(new(created, "list"), null, new("1700000000", "detail"), null),
            [new("PO%_1", 30, "已发货", "\t"), new("PO-2", 30, null, "已发货")],
            [new(null, null, null, null, 2, null, null)], amount, "USD");

    private sealed class CancelAfterCommit(CancellationTokenSource interruption) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => interruption.CancelAsync();
    }
}
