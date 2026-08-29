namespace ProductManager.Cli.Seed;

/// <summary>
/// Case-insensitive, whitespace-trimmed name → id lookup. This is the core of idempotent seed
/// loading: before creating an entity, the loader checks the index built from what the API
/// already has (matched on the natural key — its name); only a miss creates something new.
/// </summary>
public sealed class NameIndex
{
    private readonly Dictionary<string, Guid> _byName;

    public NameIndex() => _byName = new Dictionary<string, Guid>();

    public NameIndex(IEnumerable<(string Name, Guid Id)> existing)
    {
        _byName = new Dictionary<string, Guid>();
        // First entry for a given (normalized) name wins — a seed file should not need to care
        // about pre-existing duplicate names in the API; it just adopts whichever it finds first.
        foreach (var (name, id) in existing)
        {
            var key = Normalize(name);
            if (!_byName.ContainsKey(key))
                _byName[key] = id;
        }
    }

    public int Count => _byName.Count;

    public bool TryGet(string name, out Guid id) => _byName.TryGetValue(Normalize(name), out id);

    public Guid GetOrThrow(string name) =>
        TryGet(name, out var id) ? id : throw new SeedReferenceException(name);

    /// <summary>Registers a freshly created entity so later lookups in the same run see it.</summary>
    public void Add(string name, Guid id) => _byName[Normalize(name)] = id;

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}

/// <summary>The seed file referenced a name that does not exist among what has been loaded so far.</summary>
public sealed class SeedReferenceException(string name)
    : Exception($"Seed file references '{name}', which was not found among the entities loaded so far. Check ordering and spelling.");
