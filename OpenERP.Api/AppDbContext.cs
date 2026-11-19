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
    internal DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();
    internal DbSet<AdminRefreshToken> RefreshTokens => Set<AdminRefreshToken>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminAccount>(admin =>
        {
            admin.ToTable("AdminAccounts", table => table.HasCheckConstraint("CK_AdminAccounts_Singleton", "\"Id\" = 1"));
            admin.Property(x => x.Id).ValueGeneratedNever();
            admin.Property(x => x.Name).HasMaxLength(20);
            admin.Property(x => x.Phone).HasMaxLength(20);
        });

        modelBuilder.Entity<AdminRefreshToken>(token =>
        {
            token.ToTable("RefreshTokens");
            token.HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminId);
            token.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<OrderRecord>(order =>
        {
            order.ToTable("Orders");
            order.HasKey(x => x.ErpId);
            order.HasMany(x => x.PlatformOrders).WithOne().HasForeignKey(x => x.ErpId);
            order.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ErpId);
        });

        modelBuilder.Entity<OrderSyncSettings>(settings =>
        {
            settings.ToTable("OrderSyncSettings",
                table => table.HasCheckConstraint("CK_OrderSyncSettings_Singleton", "\"Id\" = 1"));
            settings.HasKey(x => x.Id);
            settings.Property(x => x.Id).ValueGeneratedNever();
            settings.Property(x => x.ShopSids).HasColumnType("integer[]").IsRequired();
        });

        modelBuilder.Entity<ErpSettings>(settings =>
        {
            settings.ToTable("ErpSettings", table => table.HasCheckConstraint("CK_ErpSettings_Singleton", "\"Id\" = 1"));
            settings.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<PlatformOrderRecord>(platformOrder =>
        {
            platformOrder.ToTable("PlatformOrders");
            platformOrder.HasKey(x => new { x.ErpId, x.PoNumber });
        });

        modelBuilder.Entity<Shop>(shop =>
        {
            shop.ToTable("Shops");
            shop.HasKey(x => x.Sid);
            shop.Property(x => x.Sid).ValueGeneratedNever();
            shop.Property(x => x.Name).HasMaxLength(200);
            shop.Property(x => x.Platform).HasMaxLength(64);
            shop.Property(x => x.Region).HasMaxLength(64);
            shop.Property(x => x.MemberId).HasMaxLength(128);
            shop.Property(x => x.TokenError).HasMaxLength(500);
            shop.HasIndex(x => new { x.Platform, x.IsTokenOk });
        });

        modelBuilder.Entity<ShopPlatform>(platform =>
        {
            platform.ToTable("ShopPlatforms");
            platform.HasKey(x => x.Name);
            platform.Property(x => x.Name).HasMaxLength(64);
            platform.Property(x => x.DisplayName).HasMaxLength(100);
            platform.Property(x => x.Icon).HasMaxLength(100);
        });

        modelBuilder.Entity<ShopSyncState>(state =>
        {
            state.ToTable("ShopSyncStates",
                table => table.HasCheckConstraint("CK_ShopSyncStates_Singleton", "\"Id\" = 1"));
            state.HasKey(x => x.Id);
            state.Property(x => x.Id).ValueGeneratedNever();
            state.Property(x => x.LastError).HasMaxLength(500);
        });

        modelBuilder.Entity<OrderLineRecord>(line =>
        {
            line.ToTable("OrderLines");
            line.HasKey(x => new { x.ErpId, x.Ordinal });
        });
    }
}
