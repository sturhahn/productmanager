namespace ProductManager.Domain.Common;

/// <summary>Base for all persisted entities: Guid identity.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}
