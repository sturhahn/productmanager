namespace ProductManager.Cli.Client;

// Mirrors ProductManager.Domain.Common enums. Kept as independent copies (rather than a project
// reference to the Domain assembly) so the CLI stays a pure HTTP client of the documented
// contract, matching the wire format (enums travel as their names — see openapi/productmanager.json).

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

/// <summary>Coverage tier vocabulary. Unknown is not a tier — see <see cref="CoverageDto.IsUnknown"/>.</summary>
public enum CoverageTier
{
    NotPlaying,
    Bronze,
    Silver,
    Gold
}

public enum FeatureDeltaKind
{
    Coverage,
    SwitchingCost,
    Other
}

public enum SequencingViolationKind
{
    StartsBeforeDependencyEnds,
    DependencyNotOnRoadmap,
    DependencyCycle
}

// ---------- Phase 2: market dynamics / simulation ----------

public enum SimulationFlowKind
{
    NewFirmArrival,
    ConversionToMarket,
    WinRate,
    ChurnHazard,
    CrossSellRate
}

/// <summary>Domain-side distribution family vocabulary — note "Lognormal" (one word). The engine's
/// own copy of this enum spells it "LogNormal"; see <see cref="EngineDistributionType"/>.</summary>
public enum DistributionType
{
    Point,
    Normal,
    Lognormal,
    Beta
}

/// <summary>Engine-side distribution family vocabulary (OpenApiSchemaIds prefixes Engine types to
/// disambiguate from the Domain's own <see cref="DistributionType"/>, which spells "Lognormal"
/// differently). Only <see cref="EngineDistributionSpec"/> uses this.</summary>
public enum EngineDistributionType
{
    Normal,
    LogNormal,
    Beta,
    Point
}

public enum ParameterProvenance
{
    Asserted,
    Calibrated,
    Default
}

public enum FactEventKind
{
    ClientWon,
    ClientLost,
    Churn,
    CompetitorShippedFeature,
    PricingChanged,
    IncidentOccurred,
    CovariateObserved,
    Other,
    OpportunityDecided,
    SegmentArrivalObserved
}

public enum EpistemicStatus
{
    Observed,
    Inferred,
    Synthetic
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

// ---------- Phase 3: firm-level choice engine ----------

/// <summary>Opportunity lifecycle: open -&gt; qualified -&gt; decided (Won / Lost / NoDecision).
/// "Outcome" on a decide request reuses this same enum — the outcome IS the terminal stage.</summary>
public enum OpportunityStage
{
    Open,
    Qualified,
    Won,
    Lost,
    NoDecision
}

/// <summary>Which engine executes a simulation run. Both modes emit the same trajectory shape.</summary>
public enum SimulationEngineMode
{
    Aggregate,
    FirmLevel
}
