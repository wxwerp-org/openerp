using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenERP.Api.Erp;

namespace OpenERP.Api.Persistence;

internal sealed class ShopConfiguration : EntityConfiguration<Shop>
{
    public override void Configure(EntityTypeBuilder<Shop> builder)
    {
        base.Configure(builder);
        builder.ToTable("Shops");
        builder.Property(shop => shop.Name).HasMaxLength(200);
        builder.Property(shop => shop.Platform).HasMaxLength(64);
        builder.Property(shop => shop.Region).HasMaxLength(64);
        builder.Property(shop => shop.MemberId).HasMaxLength(128);
        builder.Property(shop => shop.TokenError).HasMaxLength(500);
        builder.HasIndex(shop => shop.Sid).IsUnique();
        builder.HasIndex(shop => new { shop.Platform, shop.IsTokenOk });
    }
}

internal sealed class ShopPlatformConfiguration : EntityConfiguration<ShopPlatform>
{
    public override void Configure(EntityTypeBuilder<ShopPlatform> builder)
    {
        base.Configure(builder);
        builder.ToTable("ShopPlatforms");
        builder.Property(platform => platform.Name).HasMaxLength(64);
        builder.Property(platform => platform.DisplayName).HasMaxLength(100);
        builder.Property(platform => platform.Icon).HasMaxLength(100);
        builder.HasIndex(platform => platform.Name).IsUnique();
    }
}

internal sealed class ShopSyncStateConfiguration : EntityConfiguration<ShopSyncState>
{
    public override void Configure(EntityTypeBuilder<ShopSyncState> builder)
    {
        base.Configure(builder);
        ConfigureSingleton(builder, "ShopSyncStates");
        builder.Property(state => state.LastError).HasMaxLength(500);
    }
}

internal sealed class ErpSettingsConfiguration : EntityConfiguration<ErpSettings>
{
    public override void Configure(EntityTypeBuilder<ErpSettings> builder)
    {
        base.Configure(builder);
        ConfigureSingleton(builder, "ErpSettings");
    }
}
