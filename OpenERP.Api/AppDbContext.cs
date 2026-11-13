using Microsoft.EntityFrameworkCore;
using OpenERP.Api.Orders;

namespace OpenERP.Api;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    internal DbSet<OrderRecord> Orders => Set<OrderRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderRecord>(order =>
        {
            order.ToTable("Orders");
            order.HasKey(x => x.ErpId);
            order.HasMany(x => x.PlatformOrders).WithOne().HasForeignKey(x => x.ErpId);
            order.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ErpId);
        });

        modelBuilder.Entity<PlatformOrderRecord>(platformOrder =>
        {
            platformOrder.ToTable("PlatformOrders");
            platformOrder.HasKey(x => new { x.ErpId, x.PoNumber });
        });

        modelBuilder.Entity<OrderLineRecord>(line =>
        {
            line.ToTable("OrderLines");
            line.HasKey(x => new { x.ErpId, x.Ordinal });
        });
    }
}
