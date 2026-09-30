namespace ProductManager.Cli.Seed;

/// <summary>
/// Case-insensitive, whitespace-trimmed name → id lookup. This is the core of idempotent seed
/// loading: before creating an entity, the loader checks the index built from what the API
/// already has (matched on the natural key — its name); only a miss creates something new.
/// </summary>
public sealed class NameIndex
{
    private readonly Dictionary<string, Guid> _byName;
    private readonly HashSet<string> _ambiguous = [];

    public NameIndex() => _byName = new Dictionary<string, Guid>();

    public NameIndex(IEnumerable<(string Name, Guid Id)> existing)
    {
        _byName = new Dictionary<string, Guid>();
        // First entry for a given (normalized) name wins — a seed file should not need to care
        // about pre-existing duplicate names in the API; it just adopts whichever it finds first.
        // The duplicates are remembered, though, for callers that must not guess (firm names).
        foreach (var (name, id) in existing)
        {
            var key = Normalize(name);
            if (!_byName.TryGetValue(key, out var first))
                _byName[key] = id;
            else if (first != id)
                _ambiguous.Add(key);
        }
    }

    /// <summary>True when the listing this index was built from held several ids under this name.</summary>
    public bool IsAmbiguous(string name) => _ambiguous.Contains(Normalize(name));

    public int Count => _byName.Count;

    /// <summary>
    /// Every entry, as (normalized name, id). Names come back normalized — trimmed and
    /// lower-cased — which is exactly what lookups normalize to, so an enumerated name is a valid
    /// key to look up (or to compose a composite key from, as the loader does for approaches).
    /// Materialized, so the caller may keep adding to this index while iterating.
    /// </summary>
    public IReadOnlyList<(string Name, Guid Id)> Entries =>
        _byName.Select(entry => (entry.Key, entry.Value)).ToList();

    public bool TryGet(string name, out Guid id) => _byName.TryGetValue(Normalize(name), out id);

    public Guid GetOrThrow(string name) =>
        TryGet(name, out var id) ? id : throw new SeedReferenceException(name);

    /// <summary>Registers a freshly created entity so later lookups in the same run see it.</summary>
    public void Add(string name, Guid id) => _byName[Normalize(name)] = id;

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}

/// <summary>The seed file referenced a name that does not exist among what has been loaded so far.</summary>
public sealed class SeedReferenceException : Exception
{
    public SeedReferenceException(string name)
        : base($"Seed file references '{name}', which was not found among the entities loaded so far. Check ordering and spelling.")
    {
    }

    private SeedReferenceException(string message, Exception? inner) : base(message, inner)
    {
    }

    /// <summary>
    /// A reference problem that is not a plain "this name is missing" — e.g. the file omits its
    /// <c>market</c> section while the workspace holds several markets, so the loader cannot tell
    /// which one the file's segment/product/process names belong to.
    /// </summary>
    public static SeedReferenceException WithMessage(string message) => new(message, inner: null);
}
