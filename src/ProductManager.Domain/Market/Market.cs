using ProductManager.Domain.Common;

namespace ProductManager.Domain.Market;

/// <summary>A competitive arena: contains segments, a business-process catalog, and products in competition.</summary>
public sealed class Market : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; set; }

    private Market() { } // EF

    public Market(Guid workspaceId, string name, string? description = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        Rename(name);
        WorkspaceId = workspaceId;
        Description = description;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Market name is required.", nameof(name));
        Name = name.Trim();
    }
}

/// <summary>
/// Group of firms with a shared profile. Segment evolution (e.g. ETF→SMA shift) = a new
/// segment in the same market with arrivals routed disproportionately to it — data, not schema.
/// </summary>
public sealed class ClientSegment : Entity
{
    private readonly List<SegmentModulePenetration> _modulePenetrations = [];
    private readonly List<SegmentApproachAdoption> _approachAdoptions = [];

    public Guid MarketId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; set; }

    /// <summary>Opportunity-scoping tendency: 0 = pure best-of-breed (narrow scopes), 1 = pure suite (broad scopes).</summary>
    public double SuiteAffinity { get; private set; } = 0.5;

    /// <summary>Preference for consolidating vendors (0..1).</summary>
    public double ConsolidationPreference { get; private set; } = 0.5;

    /// <summary>
    /// Observed starting stocks and unit economics — what the simulation engine starts a run from.
    /// All-zero until someone measures the segment.
    /// </summary>
    public SegmentStockState StockState { get; private set; } = SegmentStockState.Zero();

    /// <summary>Per-module starting penetration and pricing inside this segment.</summary>
    public IReadOnlyCollection<SegmentModulePenetration> ModulePenetrations => _modulePenetrations.AsReadOnly();

    /// <summary>
    /// How this segment's firms split across the alternative approaches to a process (e.g. 99%
    /// DTCC / 1% chain). Empty means one implicit approach — the common case. A firm's effective
    /// coverage is the tier weighted by this mix, which is what stops "gold on blockchain × 1%
    /// adoption" from reading as gold.
    /// </summary>
    public IReadOnlyCollection<SegmentApproachAdoption> ApproachAdoptions => _approachAdoptions.AsReadOnly();

    private ClientSegment() { } // EF

    public ClientSegment(Guid marketId, string name, string? description = null)
    {
        if (marketId == Guid.Empty) throw new ArgumentException("MarketId is required.", nameof(marketId));
        Rename(name);
        MarketId = marketId;
        Description = description;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Segment name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetTendencies(double suiteAffinity, double consolidationPreference)
    {
        if (suiteAffinity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(suiteAffinity), "Must be in [0, 1].");
        if (consolidationPreference is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(consolidationPreference), "Must be in [0, 1].");
        SuiteAffinity = suiteAffinity;
        ConsolidationPreference = consolidationPreference;
    }

    /// <summary>Replaces the observed starting state wholesale — it is a single measurement, not a set of knobs.</summary>
    public void SetStockState(SegmentStockState stockState) =>
        StockState = stockState ?? throw new ArgumentNullException(nameof(stockState));

    /// <summary>Sets (upserting by module) the starting penetration and pricing of one module.</summary>
    public SegmentModulePenetration SetModulePenetration(Guid moduleProductId, double penetration, double arpuPerMonth)
    {
        var row = new SegmentModulePenetration(moduleProductId, penetration, arpuPerMonth);
        _modulePenetrations.RemoveAll(m => m.ModuleProductId == moduleProductId);
        _modulePenetrations.Add(row);
        return row;
    }

    /// <summary>Drops a module from the segment's starting state. Returns false when it was not there.</summary>
    public bool RemoveModulePenetration(Guid moduleProductId) =>
        _modulePenetrations.RemoveAll(m => m.ModuleProductId == moduleProductId) > 0;

    /// <summary>Sets (upserting by approach) the share of this segment's firms using one approach.</summary>
    public SegmentApproachAdoption SetApproachAdoption(Guid approachId, double share)
    {
        var row = new SegmentApproachAdoption(approachId, share);
        _approachAdoptions.RemoveAll(a => a.ApproachId == approachId);
        _approachAdoptions.Add(row);
        return row;
    }

    /// <summary>Drops one approach from the segment's adoption mix. False when it was not there.</summary>
    public bool RemoveApproachAdoption(Guid approachId) =>
        _approachAdoptions.RemoveAll(a => a.ApproachId == approachId) > 0;
}

/// <summary>
/// Any market participant: prospect, our client, competitor's client, or synthetic (generated
/// to fill the unobserved market). Epistemic status is carried at the firm level in Phase 1;
/// per-attribute posteriors arrive with the calibration machinery.
/// </summary>
public sealed class Firm : Entity
{
    public Guid MarketId { get; private set; }
    public Guid? ClientSegmentId { get; set; }
    public string Name { get; private set; } = null!;
    public EpistemicStatus EpistemicStatus { get; private set; }

    /// <summary>Free-form attributes (JSON): industry specifics live in data, never in schema.</summary>
    public string? AttributesJson { get; set; }

    public string? Notes { get; set; }

    private Firm() { } // EF

    public Firm(Guid marketId, string name, EpistemicStatus epistemicStatus, Guid? clientSegmentId = null)
    {
        if (marketId == Guid.Empty) throw new ArgumentException("MarketId is required.", nameof(marketId));
        Rename(name);
        MarketId = marketId;
        EpistemicStatus = epistemicStatus;
        ClientSegmentId = clientSegmentId;
    }

    /// <summary>
    /// A generated firm filling part of the unobserved market, with a caller-supplied identity.
    /// The id is an argument rather than a fresh Guid so generation can be *deterministic*:
    /// regenerating a market's synthetic population from the same stocks must produce the same
    /// firms, not a second population beside the first.
    /// </summary>
    public static Firm Synthetic(Guid id, Guid marketId, Guid clientSegmentId, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("A synthetic firm needs an id.", nameof(id));
        if (clientSegmentId == Guid.Empty)
            throw new ArgumentException("A synthetic firm belongs to a segment.", nameof(clientSegmentId));
        return new Firm(marketId, name, EpistemicStatus.Synthetic, clientSegmentId) { Id = id };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Firm name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetEpistemicStatus(EpistemicStatus status) => EpistemicStatus = status;
}

/// <summary>Organization owning rival products.</summary>
public sealed class Competitor : Entity
{
    public Guid MarketId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? StrategyNotes { get; set; }

    private Competitor() { } // EF

    public Competitor(Guid marketId, string name)
    {
        if (marketId == Guid.Empty) throw new ArgumentException("MarketId is required.", nameof(marketId));
        Rename(name);
        MarketId = marketId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Competitor name is required.", nameof(name));
        Name = name.Trim();
    }
}
