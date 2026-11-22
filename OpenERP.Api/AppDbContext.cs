using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
