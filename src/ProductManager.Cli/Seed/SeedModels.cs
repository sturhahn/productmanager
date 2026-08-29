namespace ProductManager.Cli.Seed;

// Plain POCOs mirroring seed/phase1-seed.json. References between entities are by natural key
// (name) rather than id, so the same file can be loaded repeatedly against a fresh or
// partially-seeded API. Deserialized with camelCase JSON property matching (see SeedLoader).

public sealed class SeedFile
{
    public SeedWorkspace? Workspace { get; set; }
    public List<SeedTeam> Teams { get; set; } = [];
    public List<SeedInitiative> Initiatives { get; set; } = [];
    public List<SeedProject> Projects { get; set; } = [];
    public List<SeedFeature> Features { get; set; } = [];
    public SeedRoadmap? Roadmap { get; set; }
    public SeedMarket? Market { get; set; }
    public SeedMarketDynamics? MarketDynamics { get; set; }
}

public sealed class SeedWorkspace
{
    public string Name { get; set; } = "";
}

public sealed class SeedCapacity
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Points { get; set; }
}

public sealed class SeedTeam
{
    public string Name { get; set; } = "";
    public string? SpecializationTag { get; set; }
    public List<SeedCapacity> Capacities { get; set; } = [];
}

public sealed class SeedInitiative
{
    public string Name { get; set; } = "";
    public string? Outcome { get; set; }
    public string? HypothesizedValue { get; set; }
    public List<string> TargetSegmentNames { get; set; } = [];
}

public sealed class SeedEffortDemand
{
    public string TeamName { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Points { get; set; }
}

public sealed class SeedProject
{
    public string Name { get; set; } = "";
    public string InitiativeName { get; set; } = "";
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public List<string> DependsOnProjectNames { get; set; } = [];
    public List<SeedEffortDemand> EffortDemands { get; set; } = [];
    public List<string> DeliversFeatureNames { get; set; } = [];
}

public sealed class SeedFeatureDelta
{
    public string Kind { get; set; } = "Coverage";
    public string? BusinessProcessName { get; set; }
    public string? FromTier { get; set; }
    public string? ToTier { get; set; }
    public string? ApproachName { get; set; }
    public string? SegmentName { get; set; }
    public double? Magnitude { get; set; }
    public string? Rationale { get; set; }
}

public sealed class SeedFeature
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? ShippedDate { get; set; }
    public string? ProductName { get; set; }
    public List<SeedFeatureDelta> Deltas { get; set; } = [];
}

public sealed class SeedRoadmapItem
{
    public string ProjectName { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public int? SortOrder { get; set; }
}

public sealed class SeedRoadmap
{
    public string Name { get; set; } = "";
    public string? VersionName { get; set; }
    public bool IsLive { get; set; } = true;
    public List<SeedRoadmapItem> Items { get; set; } = [];
}

public sealed class SeedSegment
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public double SuiteAffinity { get; set; } = 0.5;
    public double ConsolidationPreference { get; set; } = 0.5;
}

public sealed class SeedBusinessProcess
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? ParentProcessName { get; set; }
}

public sealed class SeedApproach
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string BusinessProcessName { get; set; } = "";
}

public sealed class SeedCompetitor
{
    public string Name { get; set; } = "";
    public string? StrategyNotes { get; set; }
}

public sealed class SeedPrice
{
    public string? SegmentName { get; set; }
    public decimal RecurringAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal ImplementationFeeAmount { get; set; }
    public string? ImplementationFeeCurrency { get; set; }
    public string? UsageBasedPlaceholder { get; set; }
}

public sealed class SeedProduct
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CompetitorName { get; set; }
    public string? ParentProductName { get; set; }
    public List<SeedPrice> Prices { get; set; } = [];
}

public sealed class SeedCoverage
{
    public string ProductName { get; set; } = "";
    public string BusinessProcessName { get; set; } = "";
    public string? SegmentName { get; set; }
    public string? ApproachName { get; set; }
    public string Tier { get; set; } = "NotPlaying";
    public bool IsUnknown { get; set; }
    public double Confidence { get; set; } = 1.0;
    public string? Rationale { get; set; }
    public string? EffectiveDate { get; set; }
}

public sealed class SeedMarket
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<SeedSegment> Segments { get; set; } = [];
    public List<SeedBusinessProcess> BusinessProcesses { get; set; } = [];
    public List<SeedApproach> Approaches { get; set; } = [];
    public List<SeedCompetitor> Competitors { get; set; } = [];
    public List<SeedProduct> Products { get; set; } = [];
    public List<SeedCoverage> Coverages { get; set; } = [];
}

// ---------- Phase 2: market dynamics / simulation ----------
//
// Layered on top of an already-seeded market (referenced by name, not redefined) so
// "phase2-seed.json" can be loaded after "phase1-seed.json" without redeclaring the market
// skeleton. Every reference below (segment/product/competitor/covariate names) is resolved
// against what the API already has, the same way Coverages resolve against the market seeded in
// the same run.

public sealed class SeedModulePenetration
{
    public string ModuleProductName { get; set; } = "";
    public double Penetration { get; set; }
    public double ArpuPerMonth { get; set; }
}

public sealed class SeedSegmentStockState
{
    public string SegmentName { get; set; } = "";
    public double InitialProspectPool { get; set; }
    public int InitialClientCount { get; set; }
    public double ArpuPerMonth { get; set; }
    public double ImplementationFee { get; set; }
    public double ServicingCostPerClient { get; set; }
    public double InfraFixedCostPerMonth { get; set; }
    public double InfraCostPerClient { get; set; }
    public double RdDragPerMonth { get; set; }
    public List<SeedModulePenetration> ModulePenetrations { get; set; } = [];
}

public sealed class SeedCovariateObservation
{
    public string EffectiveDate { get; set; } = "";
    public double Value { get; set; }
    public string? Notes { get; set; }
}

public sealed class SeedCovariate
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public double? GbmStart { get; set; }
    public double? GbmDrift { get; set; }
    public double? GbmVol { get; set; }
    public List<SeedCovariateObservation> Observations { get; set; } = [];
}

public sealed class SeedParameter
{
    /// <summary>Engine parameter: give SegmentName + FlowKind (+ ModuleProductName for
    /// CrossSellRate). Free-form parameter: give Key instead.</summary>
    public string? SegmentName { get; set; }
    public string? FlowKind { get; set; }
    public string? ModuleProductName { get; set; }
    public string? Key { get; set; }
    public string DistType { get; set; } = "Point";
    public double P1 { get; set; }
    public double P2 { get; set; }
    public string Provenance { get; set; } = "Asserted";
    public string? Rationale { get; set; }
    public string? CovariateName { get; set; }
    public double? CovariateBeta { get; set; }
}

public sealed class SeedAssumptionSet
{
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public List<SeedParameter> Parameters { get; set; } = [];
}

public sealed class SeedEventRateMultiplier
{
    /// <summary>Omit for "applies to every segment".</summary>
    public string? SegmentName { get; set; }
    public string FlowKind { get; set; } = "";
    public double Multiplier { get; set; }
}

public sealed class SeedMarketEvent
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Other";
    public string StartDate { get; set; } = "";
    public string? EndDate { get; set; }
    public bool IsHypothetical { get; set; }
    public double? Magnitude { get; set; }
    public string? CompetitorName { get; set; }
    public string? Notes { get; set; }
    public List<SeedEventRateMultiplier> RateMultipliers { get; set; } = [];
}

public sealed class SeedMarketDynamics
{
    /// <summary>The already-seeded market these entities attach to.</summary>
    public string MarketName { get; set; } = "";
    public List<SeedSegmentStockState> SegmentStockStates { get; set; } = [];
    public List<SeedCovariate> Covariates { get; set; } = [];
    public List<SeedAssumptionSet> AssumptionSets { get; set; } = [];
    public List<SeedMarketEvent> MarketEvents { get; set; } = [];
}
