using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderStorePostgresTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string _databaseName = $"openerp_orders_test_{Guid.NewGuid():N}";
    private string _adminConnectionString = "";
    private string _testConnectionString = "";
    private bool _databaseCreated;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _adminConnectionString = await fixture.GetPostgresConnectionStringAsync(cancellationToken);
        _testConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
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
    public async Task Migration_creates_all_order_tables_in_a_new_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        await db.Database.MigrateAsync(cancellationToken);

        await using var connection = new NpgsqlConnection(_testConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('Orders', 'PlatformOrders', 'OrderLines')
            """, connection);
        Assert.Equal(3L, Assert.IsType<long>(await command.ExecuteScalarAsync(cancellationToken)));
    }

    [Fact]
    public async Task Saves_orders_and_updates_only_the_orders_returned_by_the_erp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await MigrateAsync(cancellationToken);
        var order = CreateOrder("erp-1");
        var anotherOrder = CreateOrder("erp-2");

        Assert.Equal(new OrderSaveCounts(2, 0, 0), await SaveAsync([order, anotherOrder], cancellationToken));
        var changedLine = order.Lines[0] with { Quantity = 7, Disabled = true, Appended = false };
        var changed = order with
        {
            Status = "dispatched",
            Times = order.Times with { Dispatched = new("2026-11-12 01:02:03", "platform") },
            PlatformOrders = [new("po-1", 30, "10", "已发货"), new("po-3", 30, null, null)],
            Lines = [changedLine, changedLine]
        };

        Assert.Equal(new OrderSaveCounts(0, 1, 0), await SaveAsync([changed], cancellationToken));

        await using var db = CreateContext();
        Assert.Equal(2L, await CountAsync(db, """
            SELECT count(*) AS "Value" FROM "Orders" WHERE "OrderNumber" = 'shared-number'
            """, cancellationToken));
        Assert.Equal(1L, await CountAsync(db, """
            SELECT count(*) AS "Value" FROM "Orders"
            WHERE "ErpId" = 'erp-1' AND "Sid" = 30 AND "Platform" = 'Takealot' AND "Status" = 'dispatched'
              AND "CreatedRaw" = '1700000000' AND "CreatedSource" = 'addTime'
              AND "PaidRaw" IS NULL AND "PaidSource" IS NULL
              AND "DispatchedRaw" = '2026-11-12 01:02:03' AND "DispatchedSource" = 'platform'
              AND "RefundedRaw" IS NULL AND "RefundedSource" IS NULL
            """, cancellationToken));
        Assert.Equal(1L, await CountAsync(db, """
            SELECT count(*) AS "Value" FROM "Orders" WHERE "ErpId" = 'erp-2' AND "Status" = 'pending'
            """, cancellationToken));
        Assert.Equal(2L, await CountAsync(db, """
            SELECT count(*) AS "Value" FROM "PlatformOrders" WHERE "ErpId" = 'erp-1'
            """, cancellationToken));
        Assert.Equal(2L, await CountAsync(db, """
            SELECT count(*) AS "Value" FROM "PlatformOrders" WHERE "ErpId" = 'erp-1' AND "Sid" = 30
              AND (("PoNumber" = 'po-1' AND "StatusCode" = '10' AND "StatusText" = '已发货')
                OR ("PoNumber" = 'po-3' AND "StatusCode" IS NULL AND "StatusText" IS NULL))
            """, cancellationToken));
        Assert.Equal(2L, await CountAsync(db, """
            SELECT count(*) AS "Value" FROM "OrderLines" WHERE "ErpId" = 'erp-1'
            """, cancellationToken));
        Assert.Equal(2L, await CountAsync(db, """
            SELECT count(*) AS "Value" FROM "OrderLines"
            WHERE "ErpId" = 'erp-1' AND "ProductId" = 'product-1' AND "Oguid" = 'oguid-1'
              AND "SkuId" = 'sku-id-1' AND "SkuCode" = 'sku-1' AND "Quantity" = 7
              AND "Disabled" AND NOT "Appended"
            """, cancellationToken));
    }

    [Fact]
    public async Task Replaying_an_order_with_reordered_children_does_not_write_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await MigrateAsync(cancellationToken);
        var order = CreateOrder("erp-1");
        order = order with { Lines = [order.Lines[0], order.Lines[1], order.Lines[0]] };
        Assert.Equal(new OrderSaveCounts(1, 0, 0), await SaveAsync([order], cancellationToken));
        var versionsBefore = await ReadRowVersionsAsync(cancellationToken);
        Assert.Equal(6, versionsBefore.Length);

        var reordered = order with
        {
            PlatformOrders = order.PlatformOrders.Reverse().ToArray(),
            Lines = [order.Lines[1], order.Lines[0], order.Lines[2]]
        };

        Assert.Equal(new OrderSaveCounts(0, 0, 1), await SaveAsync([order], cancellationToken));
        Assert.Equal(new OrderSaveCounts(0, 0, 1), await SaveAsync([reordered], cancellationToken));
        Assert.Equal(versionsBefore, await ReadRowVersionsAsync(cancellationToken));
    }

    [Fact]
    public async Task A_failed_batch_rolls_back_insertions_and_updates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await MigrateAsync(cancellationToken);
        var existing = CreateOrder("erp-1");
        Assert.Equal(new OrderSaveCounts(1, 0, 0), await SaveAsync([existing], cancellationToken));
        var versionsBefore = await ReadRowVersionsAsync(cancellationToken);
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "OrderLines" ADD CONSTRAINT "TestRequiresPositiveQuantity" CHECK ("Quantity" > 0)
            """, cancellationToken);
        var invalid = CreateOrder("erp-3") with { Lines = [existing.Lines[0] with { Quantity = 0 }] };

        var result = await new OrderStore(db).SaveAsync(
            [existing with { Status = "dispatched" }, CreateOrder("erp-2"), invalid], cancellationToken);

        if (!result.IsErr)
            throw new XunitException("违反数据库约束必须返回保存失败。");
        Assert.Equal("保存订单失败，请检查数据库后重试。", result.Error.Message);
        Assert.Equal(versionsBefore, await ReadRowVersionsAsync(cancellationToken));
    }

    private static OrderSnapshot CreateOrder(string erpId) => new(
        erpId, "shared-number", 30, "Takealot", "pending", new(new("1700000000", "addTime"), null, null, null),
        [new("po-1", 30, "1", "待发货"), new("po-2", 30, null, null)],
        [new("product-1", "oguid-1", "sku-id-1", "sku-1", 3, null, null),
         new("product-2", "oguid-2", "sku-id-2", "sku-2", 1, false, true)]);

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(cancellationToken);
    }

    private async Task<OrderSaveCounts> SaveAsync(IReadOnlyList<OrderSnapshot> orders, CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        var result = await new OrderStore(db).SaveAsync(orders, cancellationToken);
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        return result.Value;
    }

    private static Task<long> CountAsync(AppDbContext db, string sql, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<long>(sql).SingleAsync(cancellationToken);

    private async Task<string[]> ReadRowVersionsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        return await db.Database.SqlQueryRaw<string>("""
            SELECT 'order:' || "ErpId" || ':' || xmin::text AS "Value" FROM "Orders"
            UNION ALL
            SELECT 'po:' || "ErpId" || ':' || "PoNumber" || ':' || xmin::text AS "Value" FROM "PlatformOrders"
            UNION ALL
            SELECT 'line:' || "ErpId" || ':' || "Ordinal"::text || ':' || xmin::text AS "Value" FROM "OrderLines"
            """)
            .OrderBy(version => version)
            .ToArrayAsync(cancellationToken);
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_testConnectionString, options => options.EnableRetryOnFailure().MaxBatchSize(1))
        .Options);
}
