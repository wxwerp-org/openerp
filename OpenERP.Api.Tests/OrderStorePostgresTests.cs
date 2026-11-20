using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            SELECT count(*) AS "Value" FROM "PlatformOrders" WHERE "ErpId" = 'erp-1' AND "DeletedAt" IS NULL
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Updating_an_order_preserves_its_uuid_and_restores_missing_children_by_business_key(bool useCache)
    {
        // Given 上游订单及其子单和明细已经同步。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = useCache ? services.BuildServiceProvider() : null;
        var original = CreateOrder("erp-1");
        Assert.Equal(new OrderSaveCounts(1, 0, 0), await SaveAsync([original], ct, provider));
        var ids = await ReadOrderIdsAsync(ct);
        Assert.Equal(5, ids.Length);
        Assert.All(ids, id => Assert.Equal(7, id.Version));

        // When 上游暂不返回其中一条子单和明细。
        var reduced = original with { Status = "dispatched", PlatformOrders = [original.PlatformOrders[0]], Lines = [original.Lines[0]] };
        Assert.Equal(new OrderSaveCounts(0, 1, 0), await SaveAsync([reduced], ct, provider));

        // Then 这些行保留原 UUID，并从默认查询排除。
        await using var db = CreateContext();
        var active = await db.Orders.AsNoTracking().Include(order => order.PlatformOrders).Include(order => order.Lines)
            .AsSplitQuery().SingleAsync(ct);
        Assert.Equal("dispatched", active.Status);
        Assert.Single(active.PlatformOrders);
        Assert.Single(active.Lines);
        Assert.Equal(ids, await ReadOrderIdsAsync(ct));

        // When 上游再次返回这些业务键。Then 原行恢复，并且 UUID 不变。
        Assert.Equal(new OrderSaveCounts(0, 1, 0), await SaveAsync([original], ct, provider));
        var restored = await db.Orders.AsNoTracking().Include(order => order.PlatformOrders).Include(order => order.Lines)
            .AsSplitQuery().SingleAsync(ct);
        Assert.Equal(2, restored.PlatformOrders.Count);
        Assert.Equal(2, restored.Lines.Count);
        Assert.Equal(ids, await ReadOrderIdsAsync(ct));
    }

    private async Task<Guid[]> ReadOrderIdsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        return await db.Database.SqlQueryRaw<Guid>("""
            SELECT "Id" AS "Value" FROM "Orders"
            UNION ALL
            SELECT "Id" AS "Value" FROM "PlatformOrders"
            UNION ALL
            SELECT "Id" AS "Value" FROM "OrderLines"
            """).OrderBy(id => id).ToArrayAsync(cancellationToken);
    }

    [Fact]
    public async Task A_failed_batch_rolls_back_insertions_and_updates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await MigrateAsync(cancellationToken);
        var existing = CreateOrder("erp-1");
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        Assert.Equal(new OrderSaveCounts(1, 0, 0), await SaveAsync([existing], cancellationToken, provider));
        var versionsBefore = await ReadRowVersionsAsync(cancellationToken);
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "OrderLines" ADD CONSTRAINT "TestRequiresPositiveQuantity" CHECK ("Quantity" > 0)
            """, cancellationToken);
        var invalid = CreateOrder("erp-3") with { Lines = [existing.Lines[0] with { Quantity = 0 }] };

        var changed = existing with { Status = "dispatched" };
        var result = await ActivatorUtilities.CreateInstance<OrderStore>(provider, db).SaveAsync(
            [changed, CreateOrder("erp-2"), invalid], cancellationToken);

        if (!result.IsErr)
            throw new XunitException("违反数据库约束必须返回保存失败。");
        Assert.Equal("保存订单失败，请检查数据库后重试。", result.Error.Message);
        Assert.Equal(versionsBefore, await ReadRowVersionsAsync(cancellationToken));

        // 修正失败的输入后整个批次可重试，只有成功提交的结果才能命中缓存。
        OrderSnapshot[] retry = [changed, CreateOrder("erp-2"), CreateOrder("erp-3")];
        Assert.Equal(new OrderSaveCounts(2, 1, 0), await SaveAsync(retry, cancellationToken, provider));
        Assert.Equal(new OrderSaveCounts(0, 0, 3), await SaveAsync(retry, cancellationToken, provider));
    }

    [Fact]
    public async Task Cached_replay_skips_loading_children_and_processes_only_new_or_changed_orders()
    {
        // Given 首次同步将两张订单完整落库，同一应用的下一轮使用新 DbContext。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var first = CreateOrder("erp-1");
        var second = CreateOrder("erp-2");
        await using (var initial = CreateContext())
        {
            var store = ActivatorUtilities.CreateInstance<OrderStore>(provider, initial);
            var inserted = await store.SaveAsync([first, second], ct);
            if (inserted.IsErr)
                throw new XunitException(inserted.Error.Message);
            Assert.Equal(new OrderSaveCounts(2, 0, 0), inserted.Value);
        }

        // When 重读相同订单，Then 计入未变，数据库不读取平台子单或商品行。
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_testConnectionString)
            .LogTo(commands.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information).Options;
        await using (var replay = new AppDbContext(options))
        {
            var store = ActivatorUtilities.CreateInstance<OrderStore>(provider, replay);
            var unchanged = await store.SaveAsync([first, second], ct);
            if (unchanged.IsErr)
                throw new XunitException(unchanged.Error.Message);
            Assert.Equal(new OrderSaveCounts(0, 0, 2), unchanged.Value);
            Assert.DoesNotContain(commands, sql => sql.Contains("\"OrderLines\"") || sql.Contains("\"PlatformOrders\""));
        }

        // When 仅第二张订单的商品数量变化并出现新订单，Then 第一张继续跳过明细加载。
        var changed = second with { Lines = [second.Lines[0] with { Quantity = 99 }] };
        await using var mixed = CreateContext();
        var next = ActivatorUtilities.CreateInstance<OrderStore>(provider, mixed);
        var result = await next.SaveAsync([first, changed, CreateOrder("erp-3")], ct);
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        Assert.Equal(new OrderSaveCounts(1, 1, 1), result.Value);
        Assert.DoesNotContain(mixed.ChangeTracker.Entries<OrderRecord>(), entry => entry.Entity.ErpId == first.ErpId);
        Assert.Equal(99, await mixed.Set<OrderLineRecord>().Where(row => row.ErpId == second.ErpId)
            .Select(row => row.Quantity).SingleAsync(ct));
    }

    [Fact]
    public async Task New_orders_at_the_front_do_not_make_warm_orders_load_their_children_again()
    {
        // Given 已缓存超过一个 Redis 分块的订单。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var orders = Enumerable.Range(0, 257).Select(i => CreateOrder($"erp-{i:D4}")).ToArray();
        await using (var initial = CreateContext())
        {
            var store = ActivatorUtilities.CreateInstance<OrderStore>(provider, initial);
            var saved = await store.SaveAsync(orders, ct);
            if (saved.IsErr)
                throw new XunitException(saved.Error.Message);
        }

        // When 新订单排在最前，Then 跨缓存分块移动的旧订单同样跳过明细加载。
        await using var next = CreateContext();
        var newOrder = CreateOrder("erp-9999");
        var result = await ActivatorUtilities.CreateInstance<OrderStore>(provider, next)
            .SaveAsync([newOrder, .. orders], ct);
        if (result.IsErr)
            throw new XunitException(result.Error.Message);
        Assert.Equal(new OrderSaveCounts(1, 0, 257), result.Value);
        Assert.Equal([newOrder.ErpId], next.ChangeTracker.Entries<OrderRecord>().Select(entry => entry.Entity.ErpId));
    }

    [Fact]
    public async Task Stale_cache_after_a_child_only_update_cannot_hide_the_reverse_change()
    {
        // Given 一个缓存记录了 A，另一个写入者只将该订单的商品行改为 B。
        var ct = TestContext.Current.CancellationToken;
        await MigrateAsync(ct);
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var order = CreateOrder("erp-1");
        await using (var first = CreateContext())
        {
            var saved = await ActivatorUtilities.CreateInstance<OrderStore>(provider, first).SaveAsync([order], ct);
            if (saved.IsErr)
                throw new XunitException(saved.Error.Message);
        }
        var changed = order with { Lines = [order.Lines[0] with { Disabled = true }] };
        Assert.Equal(new OrderSaveCounts(0, 1, 0), await SaveAsync([changed], ct));

        // When 上游明细恢复为 A，Then 即使指纹与旧缓存相符，也会按数据库的新版本更新明细。
        await using var next = CreateContext();
        var restored = await ActivatorUtilities.CreateInstance<OrderStore>(provider, next).SaveAsync([order], ct);
        if (restored.IsErr)
            throw new XunitException(restored.Error.Message);
        Assert.Equal(new OrderSaveCounts(0, 1, 0), restored.Value);
        var states = await next.Set<OrderLineRecord>().OrderBy(line => line.Ordinal).Select(line => line.Disabled)
            .ToArrayAsync(ct);
        Assert.Equal(order.Lines.Select(line => line.Disabled), states);
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

    private async Task<OrderSaveCounts> SaveAsync(IReadOnlyList<OrderSnapshot> orders, CancellationToken cancellationToken,
        IServiceProvider? provider = null)
    {
        await using var db = CreateContext();
        var store = provider is null ? new OrderStore(db) : ActivatorUtilities.CreateInstance<OrderStore>(provider, db);
        var result = await store.SaveAsync(orders, cancellationToken);
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
