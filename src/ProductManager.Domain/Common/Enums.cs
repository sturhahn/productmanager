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
    Other,

    /// <summary>
    /// An opportunity reached a decision (won / lost / no-decision). The payload is a complete,
    /// self-contained record of the deal — segment, channel, dates, scope, winner — so calibration
    /// can reconstruct history from the ledger alone without re-reading mutable opportunity rows.
    /// </summary>
    OpportunityDecided,

    /// <summary>
    /// A monthly arrival count observed for a segment (new firms formed, or prospects coming to
    /// market). The payload carries the segment, the flow, the count and the exposure.
    /// </summary>
    SegmentArrivalObserved
}

/// <summary>
/// Which engine a simulation run was executed by. Both modes emit the same trajectory format, so
/// comparison and charting never have to care which one produced a run.
/// </summary>
public enum SimulationEngineMode
{
    /// <summary>Engine v0: aggregate stock-and-flow per segment. The fast preview mode.</summary>
    Aggregate,

    /// <summary>Engine v1: firm-level choice model over opportunities. Emits diagnostics as well.</summary>
    FirmLevel
}

/// <summary>
/// The stock-and-flow the simulation engine drives with a parameter. Member names are kept
/// identical to <c>ProductManager.Engine.FlowKind</c> — the API maps between the two by name, so
/// the Domain never has to reference the engine assembly.
/// </summary>
public enum SimulationFlowKind
{
    /// <summary>New firms entering the segment's prospect pool per month.</summary>
    NewFirmArrival,

    /// <summary>Monthly rate at which pooled prospects come to market.</summary>
    ConversionToMarket,

    /// <summary>Per-opportunity win probability.</summary>
    WinRate,

    /// <summary>Per-client monthly churn probability.</summary>
    ChurnHazard,

    /// <summary>Per-module monthly adoption probability among clients not yet holding the module.</summary>
    CrossSellRate
}

/// <summary>What kind of market-meaning delta a Feature carries.</summary>
public enum FeatureDeltaKind
{
    Coverage,
    SwitchingCost,
    Other
}
