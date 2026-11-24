using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenERP.Api.Authentication;

namespace OpenERP.Api.Persistence;

internal sealed class UserConfiguration : EntityConfiguration<UserAccount>
{
    public override void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        base.Configure(builder);
        builder.ToTable("Users", table =>
            table.HasCheckConstraint("CK_Users_KeepAdministrator", "\"Username\" <> 'admin' OR \"DeletedAt\" IS NULL"));
        builder.Property(user => user.Username).HasMaxLength(20);
        builder.Property(user => user.Name).HasMaxLength(20);
        builder.Property(user => user.Phone).HasMaxLength(20);
        builder.HasIndex(user => user.Username).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
    }
}

internal sealed class UserRefreshTokenConfiguration : EntityConfiguration<UserRefreshToken>
{
    public override void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        base.Configure(builder);
        builder.ToTable("RefreshTokens");
        builder.HasOne(token => token.User).WithMany().HasForeignKey(token => token.UserId);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasQueryFilter(token => token.DeletedAt == null && token.User.DeletedAt == null);
    }
}

internal sealed class UserShopAssignmentConfiguration : IEntityTypeConfiguration<UserShopAssignment>
{
    public void Configure(EntityTypeBuilder<UserShopAssignment> builder)
    {
        builder.HasKey(row => new { row.UserId, row.Platform, row.Sid });
        builder.Property(row => row.Platform).HasMaxLength(64);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(row => row.UserId);
    }
}
