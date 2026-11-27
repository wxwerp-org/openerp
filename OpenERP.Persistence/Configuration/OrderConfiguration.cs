using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenERP.Api.Orders;

namespace OpenERP.Api.Persistence;

internal sealed class OrderConfiguration : EntityConfiguration<OrderRecord>
{
    public override void Configure(EntityTypeBuilder<OrderRecord> builder)
    {
        base.Configure(builder);
        builder.ToTable("Orders");
        builder.HasAlternateKey(order => order.ErpId);
        builder.HasIndex(order => new { order.CreatedAtTicks, order.ErpId }).IsDescending(true, false);
        builder.Property(order => order.IncomeCost).HasPrecision(18, 2);
        builder.Property(order => order.TotalAmount).HasPrecision(18, 2);
        builder.Property(order => order.SyncRevision).IsConcurrencyToken();
        builder.HasMany(order => order.PlatformOrders).WithOne()
            .HasForeignKey(platformOrder => platformOrder.ErpId).HasPrincipalKey(order => order.ErpId);
        builder.HasMany(order => order.Lines).WithOne()
            .HasForeignKey(line => line.ErpId).HasPrincipalKey(order => order.ErpId);
    }
}

internal sealed class PlatformOrderConfiguration : EntityConfiguration<PlatformOrderRecord>
{
    public override void Configure(EntityTypeBuilder<PlatformOrderRecord> builder)
    {
        base.Configure(builder);
        builder.ToTable("PlatformOrders");
        builder.HasIndex(order => new { order.ErpId, order.PoNumber }).IsUnique();
    }
}

internal sealed class OrderLineConfiguration : EntityConfiguration<OrderLineRecord>
{
    public override void Configure(EntityTypeBuilder<OrderLineRecord> builder)
    {
        base.Configure(builder);
        builder.ToTable("OrderLines");
        builder.Property(line => line.UnitPrice).HasPrecision(18, 2);
        builder.HasIndex(line => new { line.ErpId, line.Ordinal }).IsUnique();
    }
}

internal sealed class OrderSyncSettingsConfiguration : EntityConfiguration<OrderSyncSettings>
{
    public override void Configure(EntityTypeBuilder<OrderSyncSettings> builder)
    {
        base.Configure(builder);
        ConfigureSingleton(builder, "OrderSyncSettings");
        builder.Property(settings => settings.ShopSids).IsRequired();
    }
}
