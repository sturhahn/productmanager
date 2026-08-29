using ProductManager.Domain.Common;

namespace ProductManager.Domain.Execution;

/// <summary>Root aggregate; the future tenant boundary. Everything hangs off a Workspace.</summary>
public sealed class Workspace : Entity
{
    public string Name { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Workspace() { } // EF

    public Workspace(string name)
    {
        Rename(name);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Workspace name is required.", nameof(name));
        Name = name.Trim();
    }
}
