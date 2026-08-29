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
    public Guid MarketId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; set; }

    /// <summary>Opportunity-scoping tendency: 0 = pure best-of-breed (narrow scopes), 1 = pure suite (broad scopes).</summary>
    public double SuiteAffinity { get; private set; } = 0.5;

    /// <summary>Preference for consolidating vendors (0..1).</summary>
    public double ConsolidationPreference { get; private set; } = 0.5;

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
