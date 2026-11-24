namespace OpenERP.Api;

public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset? DeletedAt { get; set; }
}
