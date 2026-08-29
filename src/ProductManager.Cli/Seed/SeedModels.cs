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
