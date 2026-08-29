namespace ProductManager.Cli.Seed;

/// <summary>Tracks how many entities of each kind the loader created vs. matched to something
/// that already existed — the evidence that a re-run stayed idempotent.
/// <para>
/// Some seed content is not created-or-matched at all: a segment's stock state and its module
/// penetrations are PUT upserts that replace whatever was there, so there is no create/match
/// decision to record. Those are counted separately as <em>applied</em> — otherwise they do real
/// work on every load and appear nowhere in the summary.
/// </para></summary>
public sealed class SeedTally
{
    private readonly Dictionary<string, (int Created, int Matched)> _byKind = new();
    private readonly Dictionary<string, int> _appliedByKind = new();

    public void RecordCreated(string kind) => Bump(kind, created: true);

    public void RecordMatched(string kind) => Bump(kind, created: false);

    /// <summary>Records one idempotent upsert (a PUT with no create-vs-match distinction).</summary>
    public void RecordApplied(string kind) =>
        _appliedByKind[kind] = (_appliedByKind.TryGetValue(kind, out int n) ? n : 0) + 1;

    private void Bump(string kind, bool created)
    {
        var (c, m) = _byKind.TryGetValue(kind, out var v) ? v : (0, 0);
        _byKind[kind] = created ? (c + 1, m) : (c, m + 1);
    }

    public IReadOnlyDictionary<string, (int Created, int Matched)> Snapshot => _byKind;

    /// <summary>Upserts applied per kind. Disjoint from <see cref="Snapshot"/>.</summary>
    public IReadOnlyDictionary<string, int> AppliedSnapshot => _appliedByKind;
}
