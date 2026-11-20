using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderSyncRevisionMigrationTests(AspireIntegrationFixture fixture)
{
    [Fact]
    public async Task Migration_assigns_distinct_revisions_to_existing_and_new_orders()
    {
        // Given 迁移前已有两条订单。
        var ct = TestContext.Current.CancellationToken;
        var connection = new NpgsqlConnectionStringBuilder(await fixture.GetPostgresConnectionStringAsync(ct))
        {
            Database = $"openerp_order_revision_{Guid.NewGuid():N}",
            Pooling = false
        };
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection.ConnectionString).Options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261118092128_AddLocalShopDirectory", ct);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Orders" ("ErpId", "OrderNumber", "Sid", "Platform", "Status")
                VALUES ('existing-1', 'existing-1', 1, 'test', 'pending'),
                       ('existing-2', 'existing-2', 1, 'test', 'pending');
                """, ct);

            // When 应用迁移后插入一条订单，省略同步版本。
            await db.Database.MigrateAsync(ct);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Orders" ("Id", "ErpId", "OrderNumber", "Sid", "Platform", "Status")
                VALUES (uuidv7(), 'new', 'new', 1, 'test', 'pending');
                """, ct);

            // Then 已有订单与新增订单均有各自非空的同步版本。
            var revisions = await db.Database.SqlQueryRaw<Guid>("""SELECT "SyncRevision" FROM "Orders" """)
                .ToListAsync(ct);
            Assert.Equal(3, revisions.Count);
            Assert.DoesNotContain(Guid.Empty, revisions);
            Assert.Equal(3, revisions.Distinct().Count());
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await db.Database.EnsureDeletedAsync(cleanup.Token);
        }
    }
}
