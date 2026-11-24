using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenERP.Api.Authentication;
using OpenERP.Api.Persistence;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class ShopSyncMigrationTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string _databaseName = $"openerp_shop_migration_test_{Guid.NewGuid():N}";
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
    public async Task Migration_creates_local_shop_tables_without_old_authorization_tables()
    {
        // Given 空数据库。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        // When 应用全部迁移。
        await db.Database.MigrateAsync(ct);

        // Then 新店铺目录三表与索引存在，旧授权快照/事件表不存在。
        Assert.True(await TableExistsAsync("Shops", ct));
        Assert.True(await TableExistsAsync("ShopPlatforms", ct));
        Assert.True(await TableExistsAsync("ShopSyncStates", ct));
        Assert.False(await TableExistsAsync("ShopAuthorizationSnapshots", ct));
        Assert.False(await TableExistsAsync("ShopAuthorizationEvents", ct));
        Assert.True(await IndexExistsAsync("Shops", "IX_Shops_Platform_IsTokenOk", ct));
        Assert.True(await ConstraintExistsAsync("ShopSyncStates", "CK_ShopSyncStates_Singleton", ct));
        Assert.True(await ColumnExistsAsync("ShopSyncStates", "Version", ct));
    }

    private async Task<bool> TableExistsAsync(string table, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = @table)
            """, connection);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    [Fact]
    public async Task Upgrade_keeps_existing_employees_and_shops_without_granting_access()
    {
        // Given 升级前的数据库已有员工和店铺。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync("20261120055817_AddOrderSyncRevision", ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Users" ("Id", "Username", "Name") VALUES (uuidv7(), 'legacy', '原员工');
            INSERT INTO "Shops" ("Id", "Platform", "Sid", "Name") VALUES (uuidv7(), 'TemuPop', 30, '原店铺');
            """, ct);

        // When 应用店铺分配迁移。Then 保留两条业务记录，员工的初始分配为空。
        await db.Database.MigrateAsync(ct);
        Assert.Equal("原员工", (await db.Users.SingleAsync(ct)).Name);
        Assert.Equal(30, (await db.Shops.SingleAsync(ct)).Sid);
        Assert.False(await db.UserShopAssignments.AnyAsync(ct));
    }

    [Fact]
    public async Task Upgrade_preserves_populated_legacy_tables_relationships_and_constraints()
    {
        // Given 旧库已经保存管理员、刷新凭证、店铺、订单、子单、明细和所有单行设置。
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync("20261118092128_AddLocalShopDirectory", ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AdminAccounts" ("Id", "Name", "PasswordHash") VALUES (1, '原管理员', 'original-hash');
            INSERT INTO "RefreshTokens" ("Id", "AdminId", "TokenHash", "ExpiresAt")
                VALUES (gen_random_uuid(), 1, decode('1234', 'hex'), '2030-01-01T00:00:00Z');
            INSERT INTO "Shops" ("Sid", "Name", "Platform") VALUES (93, '旧店铺', 'Temu');
            INSERT INTO "ShopPlatforms" ("Name", "DisplayName", "IsProduction") VALUES ('Temu', '旧平台', true);
            INSERT INTO "Orders" ("ErpId", "OrderNumber", "Sid", "Platform", "Status")
                VALUES ('old-order', 'old-number', 93, 'Temu', 'paid');
            INSERT INTO "PlatformOrders" ("ErpId", "PoNumber", "Sid") VALUES ('old-order', 'old-po', 93);
            INSERT INTO "OrderLines" ("ErpId", "Ordinal", "Quantity") VALUES ('old-order', 0, 4);
            INSERT INTO "OrderSyncSettings" ("Id", "Enabled", "ShopSids") VALUES (1, true, ARRAY[93]);
            INSERT INTO "ErpSettings" ("Id", "ProtectedAccessToken") VALUES (1, 'original-token');
            INSERT INTO "ShopSyncStates" ("Id", "Version", "Total", "Valid", "Invalid", "Unknown")
                VALUES (1, 12, 3, 1, 1, 1);
            INSERT INTO "DataProtectionKeys" ("FriendlyName", "Xml") VALUES ('original-key', 'original-xml');
            """, ct);

        // When 原库应用后续迁移。
        await db.Database.MigrateAsync(ct);

        // Then 每个自有表使用 UUIDv7 主键，且仍可关联并读取数据。
        foreach (var table in new[]
                 {
                     "Users", "RefreshTokens", "Shops", "ShopPlatforms", "Orders", "PlatformOrders",
                     "OrderLines", "OrderSyncSettings", "ErpSettings", "ShopSyncStates"
                 })
        {
            var primaryKey = await db.Database.SqlQuery<string>($"""
                SELECT c.column_name::text AS "Value"
                FROM information_schema.table_constraints t
                JOIN information_schema.key_column_usage c USING (constraint_catalog, constraint_schema, constraint_name)
                WHERE t.table_schema = 'public' AND t.table_name = {table} AND t.constraint_type = 'PRIMARY KEY'
                """).ToArrayAsync(ct);
            Assert.Equal(["Id"], primaryKey);
            var id = Assert.IsType<Guid>(await ReadScalarAsync($"""SELECT "Id" FROM "{table}" """, ct));
            Assert.Equal(7, id.Version);
        }

        var order = await db.Orders.AsNoTracking().Include(row => row.Lines).Include(row => row.PlatformOrders)
            .AsSplitQuery().SingleAsync(ct);
        Assert.Equal("old-number", order.OrderNumber);
        Assert.Equal(93, order.Sid);
        Assert.Equal(4, Assert.Single(order.Lines).Quantity);
        Assert.Equal("old-po", Assert.Single(order.PlatformOrders).PoNumber);
        Assert.Equal("旧店铺", (await db.Shops.SingleAsync(ct)).Name);
        Assert.Equal("旧平台", (await db.ShopPlatforms.SingleAsync(ct)).DisplayName);
        Assert.Equal(12, (await db.ShopSyncStates.SingleAsync(ct)).Version);
        var sync = await new Orders.OrderSyncSettingsStore(db).GetAsync(ct);
        Assert.True(sync.Enabled);
        Assert.Equal([93], sync.ShopSids);
        Assert.Equal("original-token", await ReadScalarAsync("""SELECT "ProtectedAccessToken" FROM "ErpSettings" """, ct));
        var accounts = new AccountStore(db, new AccountWriteDbContextFactory(CreateOptions()),
            new PasswordHasher<UserAccount>(), TimeProvider.System);
        var token = Assert.IsType<UserRefreshToken>(await accounts.FindActiveRefreshTokenAsync(new byte[] { 0x12, 0x34 }, ct));
        Assert.Equal("admin", token.User.Username);
        Assert.Equal(token.User.Id, token.UserId);
        Assert.Equal("original-hash", token.User.PasswordHash);
        var protection = await db.DataProtectionKeys.SingleAsync(ct);
        Assert.Equal(1, protection.Id);
        Assert.Equal("original-xml", protection.Xml);

        // Then 使用独立 UUID 仍无法插入具有相同旧业务键或第二份单例设置的记录。
        foreach (var insert in new[]
                 {
                     """INSERT INTO "Shops" ("Id", "Sid", "Name", "Platform") VALUES (uuidv7(), 93, '重复', 'Temu')""",
                     """INSERT INTO "ShopPlatforms" ("Id", "Name", "DisplayName", "IsProduction") VALUES (uuidv7(), 'Temu', '重复', true)""",
                     """INSERT INTO "Orders" ("Id", "ErpId", "OrderNumber", "Sid", "Platform", "Status") VALUES (uuidv7(), 'old-order', 'new-number', 93, 'Temu', 'paid')""",
                     """INSERT INTO "PlatformOrders" ("Id", "ErpId", "PoNumber", "Sid") VALUES (uuidv7(), 'old-order', 'old-po', 93)""",
                     """INSERT INTO "OrderLines" ("Id", "ErpId", "Ordinal", "Quantity") VALUES (uuidv7(), 'old-order', 0, 7)""",
                     """INSERT INTO "OrderSyncSettings" ("Id", "Enabled", "ShopSids") VALUES (uuidv7(), false, '{}')""",
                     """INSERT INTO "ErpSettings" ("Id", "ProtectedAccessToken") VALUES (uuidv7(), 'duplicate-token')""",
                     """INSERT INTO "ShopSyncStates" ("Id", "Version", "Total", "Valid", "Invalid", "Unknown") VALUES (uuidv7(), 0, 0, 0, 0, 0)"""
                 })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => ReadScalarAsync(insert, ct));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        }
    }

    private async Task<object?> ReadScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<bool> ColumnExistsAsync(string table, string column, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @table AND column_name = @column)
            """, connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<bool> IndexExistsAsync(string table, string index, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_indexes
                WHERE schemaname = 'public' AND tablename = @table AND indexname = @index)
            """, connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("index", index);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<bool> ConstraintExistsAsync(string table, string constraint, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = @constraint
                  AND conrelid = @table::regclass)
            """, connection);
        command.Parameters.AddWithValue("constraint", constraint);
        command.Parameters.AddWithValue("table", $"\"{table}\"");
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseOpenErpPostgres(_connectionString).Options;

    private AppDbContext CreateDbContext() => new(CreateOptions());
}
