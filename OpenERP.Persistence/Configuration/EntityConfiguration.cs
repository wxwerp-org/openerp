using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenERP.Api.Persistence;

internal abstract class EntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity> where TEntity : Entity
{
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.HasQueryFilter(entity => entity.DeletedAt == null);
    }

    protected static void ConfigureSingleton(EntityTypeBuilder<TEntity> builder, string tableName)
    {
        builder.ToTable(tableName,
            table => table.HasCheckConstraint($"CK_{tableName}_Singleton", "\"Singleton\""));
        builder.Property<bool>("Singleton").HasDefaultValue(true).HasSentinel(false);
        builder.HasIndex("Singleton").IsUnique();
    }
}
