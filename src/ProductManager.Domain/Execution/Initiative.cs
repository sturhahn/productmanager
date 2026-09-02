using ProductManager.Domain.Common;

namespace ProductManager.Domain.Execution;

/// <summary>
/// Strategic bet — the *why*. Contains Projects. Targets segments and needs on the market side
/// (cross-aggregate references by id).
/// </summary>
public sealed class Initiative : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>The named outcome this bet is meant to produce.</summary>
    public string? Outcome { get; set; }

    /// <summary>Hypothesized value narrative (why we think this wins).</summary>
    public string? HypothesizedValue { get; set; }

    /// <summary>
    /// Competitive purpose tag — Differentiation / Parity / Retention, or unset. A label only:
    /// queryable for portfolio views, but never read by the Engine or calibration. Market impact
    /// runs entirely through the FeatureDeltas this initiative's Projects deliver.
    /// </summary>
    public StrategicIntent? StrategicIntent { get; private set; }

    /// <summary>Market-side ClientSegment ids this initiative targets.</summary>
    public List<Guid> TargetSegmentIds { get; private set; } = [];

    /// <summary>Market-side Need ids this initiative targets.</summary>
    public List<Guid> TargetNeedIds { get; private set; } = [];

    private Initiative() { } // EF

    public Initiative(Guid workspaceId, string name, string? outcome = null, string? hypothesizedValue = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        Rename(name);
        WorkspaceId = workspaceId;
        Outcome = outcome;
        HypothesizedValue = hypothesizedValue;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Initiative name is required.", nameof(name));
        Name = name.Trim();
    }

    /// <summary>Sets or clears the strategic-intent tag. Pass null to clear it.</summary>
    public void SetStrategicIntent(StrategicIntent? intent) => StrategicIntent = intent;
}
