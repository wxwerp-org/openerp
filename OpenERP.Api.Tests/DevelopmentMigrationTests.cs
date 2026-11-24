using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace OpenERP.Api.Tests;

[Collection(nameof(AspireIntegrationCollection))]
public sealed class DevelopmentMigrationTests(AspireIntegrationFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task First_start_and_restart_apply_migrations_without_failed_commands(bool databaseExists)
    {
        // Given 一个全新数据库名，覆盖已建空库和数据库尚未创建的启动方式。
        var ct = TestContext.Current.CancellationToken;
        var failures = new List<string>();
        await using var db = await CreateContextAsync(failures, ct);
        try
        {
            if (databaseExists)
                await db.GetService<IRelationalDatabaseCreator>().CreateAsync(ct);

            // When 首次启动并在同一数据库上再次启动。
            await db.MigrateDevelopmentDatabaseAsync(ct);
            var first = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
            await db.MigrateDevelopmentDatabaseAsync(ct);

            // Then 全部迁移只登记一次，期间没有执行失败的数据库命令。
            Assert.NotEmpty(first);
            Assert.Equal(db.Database.GetMigrations(), first);
            Assert.Equal(first, await db.Database.GetAppliedMigrationsAsync(ct));
            Assert.Empty(failures);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await db.Database.EnsureDeletedAsync(cleanup.Token);
        }
    }

    [Fact]
    public async Task Conflicting_schema_still_raises_and_records_a_migration_error()
    {
        // Given 已有业务表，但从未登记迁移的独立数据库。
        var ct = TestContext.Current.CancellationToken;
        var failures = new List<string>();
        await using var db = await CreateContextAsync(failures, ct);
        try
        {
            await db.Database.EnsureCreatedAsync(ct);

            // When 开发环境尝试应用初始迁移。Then 原始表冲突向上传播并记录失败命令。
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.MigrateDevelopmentDatabaseAsync(ct));
            Assert.Equal(PostgresErrorCodes.DuplicateTable, error.SqlState);
            Assert.Contains(failures, command => command.Contains("CREATE TABLE", StringComparison.Ordinal));
            Assert.Empty(await db.Database.GetAppliedMigrationsAsync(ct));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await db.Database.EnsureDeletedAsync(cleanup.Token);
        }
    }

    private async Task<AppDbContext> CreateContextAsync(List<string> failures, CancellationToken ct)
    {
        var connection = new NpgsqlConnectionStringBuilder(await fixture.GetPostgresConnectionStringAsync(ct))
        {
            Database = $"openerp_migration_{Guid.NewGuid():N}",
            Pooling = false
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseOpenErpPostgres(connection.ConnectionString)
            .LogTo((id, _) => id == RelationalEventId.CommandError,
                data => failures.Add(((CommandErrorEventData)data).Command.CommandText))
            .Options;
        return new AppDbContext(options);
    }
}
