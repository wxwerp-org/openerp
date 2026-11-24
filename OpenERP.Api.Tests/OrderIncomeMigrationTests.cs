using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenERP.Api.Orders;
using Xunit;

namespace OpenERP.Api.Tests;

public sealed class OrderIncomeMigrationScriptTests
{
    [Fact]
    public void Adds_two_nullable_columns_after_the_sync_revision_migration()
    {
        // Given 已升级到同步版本迁移的数据库模型。
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseOpenErpPostgres().Options);

        // When 生成从该版本升级的脚本。
        var script = db.GetService<IMigrator>().GenerateScript("20261120055817_AddOrderSyncRevision");

        // Then 仅添加可空金额和币种列，保留存量数据。
        Assert.Contains("""ALTER TABLE "Orders" ADD "IncomeCost" numeric(18,2);""", script);
        Assert.Contains("""ALTER TABLE "Orders" ADD "IncomeCurrency" text;""", script);
        Assert.Contains("_AddOrderIncomeAmounts", script);
    }
}

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderIncomeMigrationTests(AspireIntegrationFixture fixture)
{
    [Fact]
    public async Task Existing_orders_keep_null_amounts_and_new_orders_accept_amounts()
    {
        // Given 同步版本迁移后的旧库已经包含订单。
        var ct = TestContext.Current.CancellationToken;
        var connection = new NpgsqlConnectionStringBuilder(await fixture.GetPostgresConnectionStringAsync(ct))
        {
            Database = $"openerp_order_income_{Guid.NewGuid():N}",
            Pooling = false
        };
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseOpenErpPostgres(connection.ConnectionString).Options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261120055817_AddOrderSyncRevision", ct);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Orders" ("Id", "ErpId", "OrderNumber", "Sid", "Platform", "Status")
                VALUES (uuidv7(), 'existing', 'existing', 1, 'test', 'pending');
                """, ct);

            // When 应用新迁移并通过订单存储插入一张有金额的订单。
            await db.Database.MigrateAsync(ct);
            var order = new OrderSnapshot("new", "new", 1, "test", "pending",
                new(null, null, null, null), [], [], 13.91m, "USD");
            var result = await new OrderStore(db).SaveAsync([order], ct);
            if (result.IsErr) throw new Xunit.Sdk.XunitException(result.Error.Message);
            Assert.Equal(new OrderSaveCounts(1, 0, 0), result.Value);

            // Then 存量订单的两列仍为空，新订单完整保留已知的金额及币种。
            var rows = await db.Database.SqlQueryRaw<IncomeRow>("""SELECT "ErpId", "IncomeCost", "IncomeCurrency" FROM "Orders" """)
                .ToDictionaryAsync(row => row.ErpId, ct);
            Assert.Equal(2, rows.Count);
            Assert.Null(rows["existing"].IncomeCost);
            Assert.Null(rows["existing"].IncomeCurrency);
            Assert.Equal(13.91m, rows["new"].IncomeCost);
            Assert.Equal("USD", rows["new"].IncomeCurrency);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await db.Database.EnsureDeletedAsync(cleanup.Token);
        }
    }

    private sealed record IncomeRow(string ErpId, decimal? IncomeCost, string? IncomeCurrency);
}
