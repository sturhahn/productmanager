namespace ProductManager.Cli.Seed;

/// <summary>Tracks how many entities of each kind the loader created vs. matched to something
/// that already existed — the evidence that a re-run stayed idempotent.</summary>
public sealed class SeedTally
{
    private readonly Dictionary<string, (int Created, int Matched)> _byKind = new();

    public void RecordCreated(string kind) => Bump(kind, created: true);

    public void RecordMatched(string kind) => Bump(kind, created: false);

    private void Bump(string kind, bool created)
    {
        var (c, m) = _byKind.TryGetValue(kind, out var v) ? v : (0, 0);
        _byKind[kind] = created ? (c + 1, m) : (c, m + 1);
    }

    public IReadOnlyDictionary<string, (int Created, int Matched)> Snapshot => _byKind;
}
