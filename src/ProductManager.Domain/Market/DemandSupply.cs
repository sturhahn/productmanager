using ProductManager.Domain.Common;

namespace ProductManager.Domain.Market;

/// <summary>
/// Catalog of what firms must accomplish. Hierarchical (process → sub-process), per market;
/// the shared vocabulary demand (Need) and supply (Coverage) both point at.
/// </summary>
public sealed class BusinessProcess : Entity
{
    public Guid MarketId { get; private set; }
    public Guid? ParentProcessId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; set; }

    private BusinessProcess() { } // EF

    public BusinessProcess(Guid marketId, string name, Guid? parentProcessId = null)
    {
        if (marketId == Guid.Empty) throw new ArgumentException("MarketId is required.", nameof(marketId));
        Rename(name);
        MarketId = marketId;
        ParentProcessId = parentProcessId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Business process name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetParent(Guid? parentProcessId)
    {
        if (parentProcessId == Id)
            throw new DomainConflictException("A business process cannot be its own parent.");
        ParentProcessId = parentProcessId;
    }
}

/// <summary>
/// Optional alternative *method* for a business process (e.g. settle via partner / direct /
/// on-chain). Nullable dimension — most processes have one implicit approach. Segments carry
/// adoption mixes (as data on the segment/assumption side).
/// </summary>
public sealed class Approach : Entity
{
    public Guid BusinessProcessId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; set; }

    private Approach() { } // EF

    public Approach(Guid businessProcessId, string name)
    {
        if (businessProcessId == Guid.Empty)
            throw new ArgumentException("BusinessProcessId is required.", nameof(businessProcessId));
        Rename(name);
        BusinessProcessId = businessProcessId;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Approach name is required.", nameof(name));
        Name = name.Trim();
    }
}

/// <summary>
/// Demand edge: (firm-or-segment × BusinessProcess). Importance weight, required depth/scale,
/// conditionality, hard vs soft, workaround cost if unmet. Exactly one of FirmId / ClientSegmentId is set.
/// </summary>
public sealed class Need : Entity
{
    public Guid? FirmId { get; private set; }
    public Guid? ClientSegmentId { get; private set; }
    public Guid BusinessProcessId { get; private set; }

    /// <summary>Relative importance weight (non-negative).</summary>
    public double ImportanceWeight { get; private set; }

    /// <summary>Quantitative required depth/scale dimensions (JSON — e.g. portfolio counts, volumes). Data, not schema.</summary>
    public string? RequiredDepthJson { get; set; }

    /// <summary>Conditionality on firm attributes (e.g. "operates in X"). Free-form expression/notes.</summary>
    public string? Conditionality { get; set; }

    /// <summary>Hard constraint (disqualifying if unmet) vs soft (workaround cost applies).</summary>
    public bool IsHard { get; private set; }

    /// <summary>Cost of the workaround if this (soft) need is unmet — post-processing, point-solution vendor.</summary>
    public Money? WorkaroundCostPerPeriod { get; private set; }

    public string? Notes { get; set; }

    private Need() { } // EF

    private Need(Guid? firmId, Guid? clientSegmentId, Guid businessProcessId, double importanceWeight, bool isHard,
        Money? workaroundCostPerPeriod)
    {
        if (businessProcessId == Guid.Empty)
            throw new ArgumentException("BusinessProcessId is required.", nameof(businessProcessId));
        FirmId = firmId;
        ClientSegmentId = clientSegmentId;
        BusinessProcessId = businessProcessId;
        SetImportance(importanceWeight);
        IsHard = isHard;
        WorkaroundCostPerPeriod = workaroundCostPerPeriod;
    }

    public static Need ForFirm(Guid firmId, Guid businessProcessId, double importanceWeight, bool isHard = false,
        Money? workaroundCostPerPeriod = null)
    {
        if (firmId == Guid.Empty) throw new ArgumentException("FirmId is required.", nameof(firmId));
        return new Need(firmId, null, businessProcessId, importanceWeight, isHard, workaroundCostPerPeriod);
    }

    public static Need ForSegment(Guid clientSegmentId, Guid businessProcessId, double importanceWeight,
        bool isHard = false, Money? workaroundCostPerPeriod = null)
    {
        if (clientSegmentId == Guid.Empty)
            throw new ArgumentException("ClientSegmentId is required.", nameof(clientSegmentId));
        return new Need(null, clientSegmentId, businessProcessId, importanceWeight, isHard, workaroundCostPerPeriod);
    }

    public void SetImportance(double importanceWeight)
    {
        if (importanceWeight < 0)
            throw new ArgumentOutOfRangeException(nameof(importanceWeight), "Importance weight cannot be negative.");
        ImportanceWeight = importanceWeight;
    }

    public void SetHardness(bool isHard, Money? workaroundCostPerPeriod = null)
    {
        IsHard = isHard;
        WorkaroundCostPerPeriod = workaroundCostPerPeriod;
    }
}

/// <summary>
/// Supply assessment: (Product × BusinessProcess × Approach? × Segment?) → tier.
/// Numeric mapping underneath for choice math; confidence/unknown state — competitor data is the
/// same schema, sparser; unknown ≠ bronze.
/// </summary>
public sealed class Coverage : Entity
{
    public Guid ProductId { get; private set; }
    public Guid BusinessProcessId { get; private set; }
    public Guid? ApproachId { get; private set; }
    public Guid? ClientSegmentId { get; private set; }

    public CoverageTier Tier { get; private set; }

    /// <summary>True when the tier is not actually known (sparse competitor data). Unknown ≠ bronze.</summary>
    public bool IsUnknown { get; private set; }

    /// <summary>Confidence in the assessment, [0, 1].</summary>
    public double Confidence { get; private set; }

    /// <summary>Capacity/scale limits of the coverage (JSON/notes; data, not schema).</summary>
    public string? ScaleLimits { get; set; }

    public DateOnly? EffectiveDate { get; set; }
    public string? Rationale { get; set; }

    private Coverage() { } // EF

    private Coverage(Guid productId, Guid businessProcessId, Guid? approachId, Guid? clientSegmentId,
        CoverageTier tier, bool isUnknown, double confidence)
    {
        if (productId == Guid.Empty) throw new ArgumentException("ProductId is required.", nameof(productId));
        if (businessProcessId == Guid.Empty)
            throw new ArgumentException("BusinessProcessId is required.", nameof(businessProcessId));
        ProductId = productId;
        BusinessProcessId = businessProcessId;
        ApproachId = approachId;
        ClientSegmentId = clientSegmentId;
        Tier = tier;
        IsUnknown = isUnknown;
        SetConfidence(confidence);
    }

    public static Coverage Assessed(Guid productId, Guid businessProcessId, CoverageTier tier,
        double confidence = 1.0, Guid? approachId = null, Guid? clientSegmentId = null) =>
        new(productId, businessProcessId, approachId, clientSegmentId, tier, isUnknown: false, confidence);

    /// <summary>An explicit "we don't know" record — distinct from NotPlaying and from Bronze.</summary>
    public static Coverage Unknown(Guid productId, Guid businessProcessId,
        Guid? approachId = null, Guid? clientSegmentId = null) =>
        new(productId, businessProcessId, approachId, clientSegmentId, CoverageTier.NotPlaying,
            isUnknown: true, confidence: 0.0);

    public void Reassess(CoverageTier tier, double confidence)
    {
        Tier = tier;
        IsUnknown = false;
        SetConfidence(confidence);
    }

    /// <summary>Steps back to an explicit "we don't know" — the honest state for sparse competitor data.</summary>
    public void MarkUnknown()
    {
        Tier = CoverageTier.NotPlaying;
        IsUnknown = true;
        SetConfidence(0.0);
    }

    private void SetConfidence(double confidence)
    {
        if (confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be in [0, 1].");
        Confidence = confidence;
    }

    /// <summary>Numeric mapping underneath the tier vocabulary, for choice math. Null when unknown.</summary>
    public double? NumericScore => IsUnknown
        ? null
        : Tier switch
        {
            CoverageTier.Gold => 1.0,
            CoverageTier.Silver => 0.7,
            CoverageTier.Bronze => 0.4,
            CoverageTier.NotPlaying => 0.0,
            _ => null
        };
}
