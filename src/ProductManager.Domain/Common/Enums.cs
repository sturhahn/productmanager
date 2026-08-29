namespace ProductManager.Domain.Common;

/// <summary>Coverage tier vocabulary. Unknown is NOT a tier — see <c>Coverage.IsUnknown</c>.</summary>
public enum CoverageTier
{
    NotPlaying = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3
}

/// <summary>Epistemic status of a firm (or firm attribute source).</summary>
public enum EpistemicStatus
{
    Observed,
    Inferred,
    Synthetic
}

public enum ProjectStatus
{
    Proposed,
    Planned,
    InProgress,
    Done,
    Cancelled
}

public enum FeatureStatus
{
    Hypothetical,
    Planned,
    Shipped
}

/// <summary>Opportunity lifecycle: open → qualified → decided (won / lost / no-decision).</summary>
public enum OpportunityStage
{
    Open,
    Qualified,
    Won,
    Lost,
    NoDecision
}

public enum MarketEventType
{
    ProductionIncident,
    ForcedMigration,
    PricingModelChange,
    RegulatoryChange,
    FeatureLaunch,
    Other
}

public enum DistributionType
{
    /// <summary>A point value: zero-width special case (ParamA = value, ParamB = 0).</summary>
    Point,
    Normal,
    Lognormal,
    Beta
}

public enum ParameterProvenance
{
    Asserted,
    Calibrated,
    Default
}

/// <summary>Kind of market observation in the append-only facts ledger.</summary>
public enum FactEventKind
{
    ClientWon,
    ClientLost,
    Churn,
    CompetitorShippedFeature,
    PricingChanged,
    IncidentOccurred,
    CovariateObserved,
    Other
}

/// <summary>What kind of market-meaning delta a Feature carries.</summary>
public enum FeatureDeltaKind
{
    Coverage,
    SwitchingCost,
    Other
}
