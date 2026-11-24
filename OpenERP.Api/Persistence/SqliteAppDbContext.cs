using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OpenERP.Api.Persistence;

/// <summary>与 PostgreSQL 共用实体，单独维护 SQLite 的类型映射和迁移。</summary>
internal sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : AppDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();

    protected override void ConfigureProviderModel(ModelBuilder modelBuilder)
    {
        OrderConfiguration.ConfigureSqlite(modelBuilder);
        SqliteShopDirectorySearch.ConfigureModel(modelBuilder);
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
}

internal sealed class SqliteAppDbContextFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=:memory:").Options);
}
