using ProductManager.Domain.Common;

namespace ProductManager.Domain.Seam;

/// <summary>
/// A product *capability* (not a work item). Can be hypothetical (unbuilt) or shipped;
/// delivered by Projects. Its market meaning is a set of deltas — mostly coverage deltas
/// ("bronze → gold on process P for segment S"), occasionally other kinds (e.g. a data
/// importer = switching-cost delta).
/// </summary>
public sealed class Feature : Entity
{
    private readonly List<FeatureDelta> _deltas = [];

    public Guid WorkspaceId { get; private set; }

    /// <summary>The product this capability belongs to (nullable while still exploratory).</summary>
    public Guid? ProductId { get; set; }

    public string Name { get; private set; } = null!;
    public string? Description { get; set; }
    public FeatureStatus Status { get; private set; } = FeatureStatus.Hypothetical;

    /// <summary>Date the capability became (or is expected to become) real in the market.</summary>
    public DateOnly? ShippedDate { get; private set; }

    public IReadOnlyCollection<FeatureDelta> Deltas => _deltas.AsReadOnly();

    private Feature() { } // EF

    public Feature(Guid workspaceId, string name, Guid? productId = null)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        Rename(name);
        WorkspaceId = workspaceId;
        ProductId = productId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Feature name is required.", nameof(name));
        Name = name.Trim();
    }

    public void MarkPlanned() => Status = FeatureStatus.Planned;

    public void MarkShipped(DateOnly shippedDate)
    {
        Status = FeatureStatus.Shipped;
        ShippedDate = shippedDate;
    }

    /// <summary>Adds a coverage delta: this feature moves coverage on a process (optionally per approach/segment).</summary>
    public FeatureDelta AddCoverageDelta(Guid businessProcessId, CoverageTier? fromTier, CoverageTier toTier,
        Guid? approachId = null, Guid? segmentId = null, string? rationale = null)
    {
        if (businessProcessId == Guid.Empty)
            throw new ArgumentException("BusinessProcessId is required.", nameof(businessProcessId));
        if (fromTier.HasValue && toTier <= fromTier.Value)
            throw new ArgumentException("A coverage delta must raise the tier (toTier must exceed fromTier).");
        var delta = new FeatureDelta(FeatureDeltaKind.Coverage, businessProcessId, fromTier, toTier,
            approachId, segmentId, magnitude: null, rationale);
        _deltas.Add(delta);
        return delta;
    }

    /// <summary>Adds a non-coverage delta (e.g. switching-cost reduction) with a free magnitude.</summary>
    public FeatureDelta AddOtherDelta(FeatureDeltaKind kind, double magnitude, Guid? businessProcessId = null,
        Guid? segmentId = null, string? rationale = null)
    {
        if (kind == FeatureDeltaKind.Coverage)
            throw new ArgumentException("Use AddCoverageDelta for coverage deltas.", nameof(kind));
        var delta = new FeatureDelta(kind, businessProcessId, fromTier: null, toTier: null,
            approachId: null, segmentId, magnitude, rationale);
        _deltas.Add(delta);
        return delta;
    }

    public void RemoveDelta(FeatureDelta delta) => _deltas.Remove(delta);
}

/// <summary>
/// One unit of a feature's market meaning. Coverage kind: from/to tier on a business process
/// (optional approach/segment dimensions). Other kinds: a free magnitude. Owned by Feature.
/// </summary>
public sealed class FeatureDelta
{
    public FeatureDeltaKind Kind { get; private set; }
    public Guid? BusinessProcessId { get; private set; }
    public CoverageTier? FromTier { get; private set; }
    public CoverageTier? ToTier { get; private set; }
    public Guid? ApproachId { get; private set; }
    public Guid? SegmentId { get; private set; }

    /// <summary>Effect size for non-coverage deltas (units defined by kind, in data).</summary>
    public double? Magnitude { get; private set; }

    public string? Rationale { get; private set; }

    private FeatureDelta() { } // EF

    internal FeatureDelta(FeatureDeltaKind kind, Guid? businessProcessId, CoverageTier? fromTier, CoverageTier? toTier,
        Guid? approachId, Guid? segmentId, double? magnitude, string? rationale)
    {
        if (kind == FeatureDeltaKind.Coverage && (businessProcessId is null || toTier is null))
            throw new ArgumentException("A coverage delta requires a business process and a target tier.");
        Kind = kind;
        BusinessProcessId = businessProcessId;
        FromTier = fromTier;
        ToTier = toTier;
        ApproachId = approachId;
        SegmentId = segmentId;
        Magnitude = magnitude;
        Rationale = rationale;
    }
}
