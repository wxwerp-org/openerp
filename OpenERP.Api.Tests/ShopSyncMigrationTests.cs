using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options);
}
