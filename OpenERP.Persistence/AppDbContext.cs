using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
        if (Database.IsNpgsql() && await this.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
            await this.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken);

        await Database.MigrateAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        if (Database.IsNpgsql())
            modelBuilder.Entity<OrderRecord>().Property(order => order.SyncRevision).HasDefaultValueSql("gen_random_uuid()");
        if (Database.IsSqlite())
        {
            // SQLite 以 UTC ticks 比较时刻，授权过期与排序继续由数据库执行。
            var utcTicks = new ValueConverter<DateTimeOffset, long>(value => value.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset))
                    property.SetValueConverter(utcTicks);
        }
    }
}
