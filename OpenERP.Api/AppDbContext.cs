using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;

namespace OpenERP.Api;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    internal DbSet<UserAccount> Users => Set<UserAccount>();
    internal DbSet<UserShopAssignment> UserShopAssignments => Set<UserShopAssignment>();
    internal DbSet<UserRefreshToken> RefreshTokens => Set<UserRefreshToken>();
    internal DbSet<OrderRecord> Orders => Set<OrderRecord>();

    internal DbSet<OrderSyncSettings> OrderSyncSettings => Set<OrderSyncSettings>();
    internal DbSet<ErpSettings> ErpSettings => Set<ErpSettings>();

    internal DbSet<Shop> Shops => Set<Shop>();

    internal DbSet<ShopPlatform> ShopPlatforms => Set<ShopPlatform>();

    internal DbSet<ShopSyncState> ShopSyncStates => Set<ShopSyncState>();

    internal async Task MigrateDevelopmentDatabaseAsync(CancellationToken cancellationToken)
    {
        // Npgsql 10.0.3 在迁移前先读历史表。对已有库先让 EF 建好历史表，避免空库首启报错（npgsql/efcore.pg#3407）。
        if (await this.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
            await this.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken);

        await Database.MigrateAsync(cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.IsSqlite())
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        if (Database.IsSqlite())
        {
            modelBuilder.Entity<OrderSyncSettings>().Property(settings => settings.ShopSids).HasColumnType("TEXT");
            // SQLite 的修订号由 EF 在新建订单时生成；并发令牌配置沿用共享模型。
            modelBuilder.Entity<OrderRecord>().Property(order => order.SyncRevision)
                .HasDefaultValueSql(null)
                .HasValueGenerator<GuidValueGenerator>()
                .ValueGeneratedOnAdd();
        }
    }
}
