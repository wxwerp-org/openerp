using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenERP.Api.Orders;
using Xunit;
using Xunit.Sdk;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class OrderSyncSettingsPostgresTests(AspireIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string _databaseName = $"openerp_schedule_test_{Guid.NewGuid():N}";
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
    public async Task Migration_creates_the_singleton_settings_table_with_defaults()
    {
        // Given 一个已执行迁移的新数据库。
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(cancellationToken);

        // When 检查表、单行约束并读取默认设置。
        await using var connection = new NpgsqlConnection(_testConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var table = new NpgsqlCommand("""
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name = 'OrderSyncSettings'
            """, connection);
        await using var constraint = new NpgsqlCommand("""
            SELECT count(*) FROM pg_constraint WHERE conname = 'CK_OrderSyncSettings_Singleton'
            """, connection);
        var settings = await new OrderSyncSettingsStore(db).GetAsync(cancellationToken);

        // Then 表与单行约束存在，未保存时默认为关闭且无店铺。
        Assert.Equal(1L, Assert.IsType<long>(await table.ExecuteScalarAsync(cancellationToken)));
        Assert.Equal(1L, Assert.IsType<long>(await constraint.ExecuteScalarAsync(cancellationToken)));
        Assert.False(settings.Enabled);
        Assert.Empty(settings.ShopSids);
    }

    [Fact]
    public async Task Saves_and_reloads_one_settings_row_with_distinct_shops()
    {
        // Given 已执行迁移的数据库。
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(cancellationToken);

        // When 保存启用状态和重复、乱序的店铺。
        var saved = await new OrderSyncSettingsStore(db).SaveAsync(
            true, [644928, 30, 644928], cancellationToken);
        if (saved.IsErr)
            throw new XunitException(saved.Error.Message);
        Assert.True(saved.Value.Enabled);
        Assert.Equal([30, 644928], saved.Value.ShopSids);

        // Then 只保留一行，重新读取得到同一设置。
        await using var reloadedDb = CreateContext();
        var reloaded = await new OrderSyncSettingsStore(reloadedDb).GetAsync(cancellationToken);
        Assert.True(reloaded.Enabled);
        Assert.Equal([30, 644928], reloaded.ShopSids);

        // When 再次保存并替换启用状态与店铺。
        var updated = await new OrderSyncSettingsStore(reloadedDb).SaveAsync(
            false, [644928, 31, 30], cancellationToken);
        if (updated.IsErr)
            throw new XunitException(updated.Error.Message);

        // Then 仍只有一行，内容是最后一次保存的设置。
        Assert.False(updated.Value.Enabled);
        Assert.Equal([30, 31, 644928], updated.Value.ShopSids);
        var final = await new OrderSyncSettingsStore(reloadedDb).GetAsync(cancellationToken);
        Assert.False(final.Enabled);
        Assert.Equal([30, 31, 644928], final.ShopSids);

        // When 关闭并清空店铺。
        var cleared = await new OrderSyncSettingsStore(reloadedDb).SaveAsync(false, [], cancellationToken);
        if (cleared.IsErr)
            throw new XunitException(cleared.Error.Message);

        // Then 空数组也能原子替换，仍然只有一行。
        Assert.Empty(cleared.Value.ShopSids);
        var clearedReload = await new OrderSyncSettingsStore(reloadedDb).GetAsync(cancellationToken);
        Assert.Empty(clearedReload.ShopSids);

        await using var connection = new NpgsqlConnection(_testConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var count = new NpgsqlCommand("""SELECT count(*) FROM "OrderSyncSettings" """, connection);
        Assert.Equal(1L, Assert.IsType<long>(await count.ExecuteScalarAsync(cancellationToken)));
    }

    [Fact]
    public async Task Concurrent_first_saves_keep_one_complete_configuration()
    {
        // Given 已执行迁移的数据库和两个独立上下文。
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var migrate = CreateContext();
        await migrate.Database.MigrateAsync(cancellationToken);
        await using var first = CreateContext();
        await using var second = CreateContext();

        // When 两个请求同时首次保存不同的启用状态和店铺。
        var saves = await Task.WhenAll(
            new OrderSyncSettingsStore(first).SaveAsync(true, [30], cancellationToken),
            new OrderSyncSettingsStore(second).SaveAsync(false, [31], cancellationToken));

        // Then 两个保存都成功，最终只保留其中一份完整配置，不能合并成未提交的组合。
        Assert.All(saves, result => Assert.False(result.IsErr));
        await using var read = CreateContext();
        var final = await new OrderSyncSettingsStore(read).GetAsync(cancellationToken);
        Assert.True(
            (final.Enabled && final.ShopSids.SequenceEqual([30])) ||
            (!final.Enabled && final.ShopSids.SequenceEqual([31])),
            "并发保存产生了用户未提交的启用状态与店铺组合。");
        await using var connection = new NpgsqlConnection(_testConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var count = new NpgsqlCommand("""SELECT count(*) FROM "OrderSyncSettings" """, connection);
        Assert.Equal(1L, Assert.IsType<long>(await count.ExecuteScalarAsync(cancellationToken)));
    }

    [Fact]
    public async Task Rejects_a_second_settings_row()
    {
        // Given 已执行迁移的数据库。
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(cancellationToken);

        // When 直接插入第二行设置。
        await using var connection = new NpgsqlConnection(_testConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO "OrderSyncSettings" ("Id", "Enabled", "ShopSids") VALUES (2, false, '{}')
            """, connection);

        // Then 单行约束拒绝写入。
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(cancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_testConnectionString, options => options.EnableRetryOnFailure().MaxBatchSize(1))
        .Options);
}
