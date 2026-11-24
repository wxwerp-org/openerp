using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Authentication;
using OpenERP.Api.Erp;
using OpenERP.Api.Orders;
using OpenERP.Api.Persistence;

namespace OpenERP.Api;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected AppDbContext(DbContextOptions options) : base(options) { }

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

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ConfigureProviderModel(modelBuilder);
    }

    protected virtual void ConfigureProviderModel(ModelBuilder modelBuilder) =>
        OrderConfiguration.ConfigurePostgres(modelBuilder);
}
