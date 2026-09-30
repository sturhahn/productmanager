using ProductManager.Cli.Client;

namespace ProductManager.Cli.Seed;

/// <summary>The ledger of one seed pass: counts per entity kind for the printed summary, and one
/// <see cref="SeedRecordOutcome"/> per record for the <c>--report</c> file.
/// <para>
/// Some seed content is not created-or-matched at all: a segment's stock state and its module
/// penetrations are PUT upserts that replace whatever was there, so there is no create/match
/// decision to record. Those are counted separately as <em>applied</em> — otherwise they do real
/// work on every load and appear nowhere in the summary.
/// </para>
/// <para>
/// A <see cref="DryRun"/> pass reads the API, decides, and writes nothing: its records carry the
/// planned action and a null outcome, and ids it would have created are placeholders
/// (<see cref="Placeholder"/>) that must never reach the API. Problems that make the file unsafe to
/// apply (broken references, bad dates or enums, invalid lifecycles) collect in <see cref="Problems"/>.
/// </para></summary>
public sealed class SeedTally
{
    private readonly Dictionary<string, (int Created, int Matched)> _byKind = new();
    private readonly Dictionary<string, int> _appliedByKind = new();
    private readonly Dictionary<string, (int Updated, int Unchanged, int Exceptions)> _sourcedByKind = new();
    private readonly HashSet<Guid> _placeholders = [];

    public SeedTally(bool dryRun = false) => DryRun = dryRun;

    public bool DryRun { get; }

    public List<SeedRecordOutcome> Records { get; } = [];

    /// <summary>Blocking problems found before any write. Non-empty = nothing may be applied.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>Why the pass stopped early (a write the API refused, a lost connection), if it did.</summary>
    public string? Failure { get; set; }

    public bool HasExceptions => Records.Any(r => r.Planned == SeedAction.Exception || r.Outcome == SeedAction.Exception);

    public void RecordCreated(string kind, string? key = null, SourceRef? source = null) =>
        Record(kind, key, SeedAction.Create, SeedAction.Created, source: source);

    public void RecordMatched(string kind, string? key = null) => Record(kind, key, SeedAction.Match, SeedAction.Matched);

    /// <summary>Records one idempotent upsert (a PUT with no create-vs-match distinction).</summary>
    public void RecordApplied(string kind, string? key = null) => Record(kind, key, SeedAction.Apply, SeedAction.Applied);

    /// <summary>Records one record. <paramref name="outcome"/> is what happened; a dry run always
    /// stores null there and counts the planned action instead.</summary>
    public void Record(string kind, string? key, string planned, string? outcome, string? reason = null,
        SourceRef? source = null, IReadOnlyList<FieldConflictDto>? conflicts = null)
    {
        if (DryRun) outcome = null;
        Records.Add(new SeedRecordOutcome(kind, key, source, planned, outcome,
            reason, conflicts is { Count: > 0 } ? conflicts : null));

        switch (outcome ?? planned)
        {
            case SeedAction.Created or SeedAction.Create: Bump(kind, created: true); break;
            case SeedAction.Matched or SeedAction.Match: Bump(kind, created: false); break;
            case SeedAction.Applied or SeedAction.Apply:
                _appliedByKind[kind] = (_appliedByKind.TryGetValue(kind, out int n) ? n : 0) + 1;
                break;
            default:
                var (u, s, e) = _sourcedByKind.TryGetValue(kind, out var v) ? v : (0, 0, 0);
                _sourcedByKind[kind] = (outcome ?? planned) switch
                {
                    SeedAction.Updated or SeedAction.Update => (u + 1, s, e),
                    SeedAction.Unchanged or SeedAction.NoOp => (u, s + 1, e),
                    _ => (u, s, e + 1)
                };
                break;
        }
    }

    /// <summary>A blocking problem: the file is unsafe to apply as it stands.</summary>
    public void Block(string problem) => Problems.Add(problem);

    /// <summary>A stand-in id for something a dry run would have created.</summary>
    public Guid Placeholder()
    {
        var id = Guid.NewGuid();
        _placeholders.Add(id);
        return id;
    }

    public bool IsPlaceholder(Guid? id) => id is { } value && _placeholders.Contains(value);

    private void Bump(string kind, bool created)
    {
        var (c, m) = _byKind.TryGetValue(kind, out var v) ? v : (0, 0);
        _byKind[kind] = created ? (c + 1, m) : (c, m + 1);
    }

    public IReadOnlyDictionary<string, (int Created, int Matched)> Snapshot => _byKind;

    /// <summary>Upserts applied per kind. Disjoint from <see cref="Snapshot"/>.</summary>
    public IReadOnlyDictionary<string, int> AppliedSnapshot => _appliedByKind;

    /// <summary>Source-identity outcomes per kind (updated / unchanged / exceptions). Disjoint from the other two.</summary>
    public IReadOnlyDictionary<string, (int Updated, int Unchanged, int Exceptions)> SourcedSnapshot => _sourcedByKind;
}

/// <summary>The planned-action and outcome vocabulary of <see cref="SeedRecordOutcome"/>.</summary>
public static class SeedAction
{
    // planned
    public const string Create = "Create", Match = "Match", Apply = "Apply", Update = "Update", NoOp = "NoOp";

    // outcome (Exception is both)
    public const string Created = "Created", Matched = "Matched", Applied = "Applied", Updated = "Updated",
        Unchanged = "Unchanged", Exception = "Exception";
}

/// <summary>One record of the file: what the loader planned and what actually happened (null outcome =
/// dry run, or the pass stopped before reaching it).</summary>
public sealed record SeedRecordOutcome(
    string Kind,
    string? Key,
    SourceRef? Source,
    string Planned,
    string? Outcome,
    string? Reason,
    IReadOnlyList<FieldConflictDto>? Conflicts);
