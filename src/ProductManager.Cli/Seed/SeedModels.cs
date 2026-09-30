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

    /// <summary>Competitive purpose tag — "Differentiation" | "Parity" | "Retention"; omit for unset.</summary>
    public string? StrategicIntent { get; set; }
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

/// <summary>
/// A segment-level demand edge. The firm-level choice engine reads its importance weights from
/// these rows, so a market with coverage but no needs has a supply side and no demand side: every
/// product's coverage term is multiplied by a zero weight, and
/// <c>utility.importance-weight:{segment}:{process}</c> parameters (which only ever *override* a
/// need's point weight) never fire.
/// </summary>
public sealed class SeedNeed
{
    public string SegmentName { get; set; } = "";
    public string BusinessProcessName { get; set; } = "";
    public double ImportanceWeight { get; set; }

    /// <summary>Hard needs disqualify a product scoring below <see cref="RequiredCoverage"/>; soft ones charge the workaround cost.</summary>
    public bool IsHard { get; set; }

    /// <summary>Effective-coverage floor in [0, 1], written to the need's <c>requiredDepthJson</c>. Null = the engine's default (0.4 for a hard need, none for a soft one).</summary>
    public double? RequiredCoverage { get; set; }

    /// <summary>Monthly cost of working around this need when it is unmet, in the market's currency.</summary>
    public double? WorkaroundCostPerPeriod { get; set; }

    public string? Conditionality { get; set; }
    public string? Notes { get; set; }
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
    public List<SeedNeed> Needs { get; set; } = [];
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

    /// <summary>Passed through as the CovariateObserved fact's recordedAt so seeded readings are
    /// backdated like every other historical fact (omitted = recorded "now") — see
    /// <see cref="SeedOpportunity.RecordedDate"/>. Backdating only; a future date is refused.</summary>
    public string? RecordedDate { get; set; }
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
    /// CrossSellRate). Free-form parameter: give Key instead. Choice-model parameter (Phase 3):
    /// give ChoiceKind instead — see <see cref="ProductManager.Cli.Commands.ChoiceParameterKeys"/>
    /// for the vocabulary; SegmentName/BusinessProcessName/ModuleProductName (reused as "the
    /// product this concerns") resolve the key's ids.</summary>
    public string? SegmentName { get; set; }
    public string? FlowKind { get; set; }
    public string? ModuleProductName { get; set; }
    public string? Key { get; set; }
    public string? ChoiceKind { get; set; }
    public string? BusinessProcessName { get; set; }
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

    // ---------- Phase 3: firm-level choice engine ----------

    /// <summary>File-level default for the <c>sourceSystem</c>/<c>sourceInstance</c> of every
    /// <see cref="SeedSourceRef"/> below that leaves them out (PRD6). A row that names its own
    /// system/instance wins.</summary>
    public SeedSourceDefaults? SourceDefaults { get; set; }

    public List<SeedChannel> Channels { get; set; } = [];
    public List<SeedIntegration> Integrations { get; set; } = [];
    public List<SeedFirm> Firms { get; set; } = [];

    /// <summary>Historical facts the calibration engine fits against — RecordedDate on each entry is
    /// passed through to the API as the bi-temporal recordedAt (see SeedLoader), so a backdated
    /// calibration reads the ledger as it honestly stood on that date.</summary>
    public List<SeedOpportunity> Opportunities { get; set; } = [];
    public List<SeedChurnEvent> ChurnEvents { get; set; } = [];
    public List<SeedArrivalCount> ArrivalCounts { get; set; } = [];
}

// ---------- Phase 3: channels, integrations, firms, opportunities (historical facts) ----------

public sealed class SeedChannelSegmentReach
{
    public string SegmentName { get; set; } = "";
    public double Reach { get; set; }
    public double OriginationShare { get; set; }
}

public sealed class SeedChannelProductAffinity
{
    public string ProductName { get; set; } = "";
    public double Bias { get; set; }
}

public sealed class SeedChannel
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool OriginatesOpportunities { get; set; }
    public bool CarriesOnlyListedProducts { get; set; }
    public List<SeedChannelSegmentReach> SegmentReaches { get; set; } = [];
    public List<SeedChannelProductAffinity> ProductAffinities { get; set; } = [];
}

public sealed class SeedIntegration
{
    public string ProductAName { get; set; } = "";
    public string ProductBName { get; set; } = "";
    public double QualityScore { get; set; }
    public string? Notes { get; set; }
}

// ---------- PRD6: source identity ----------

/// <summary>The source system and instance every row in the file shares, so rows only carry
/// <c>objectKind</c> + <c>sourceRecordId</c>. Take both from the discovery manifest:
/// <c>sources[].sourceId</c> and the stable id inside <c>sources[].instance</c>.</summary>
public sealed class SeedSourceDefaults
{
    public string? SourceSystem { get; set; }
    public string? SourceInstance { get; set; }
}

/// <summary>Identity of one source record. <c>sourceSystem</c>/<c>sourceInstance</c> may be left out
/// when <c>marketDynamics.sourceDefaults</c> supplies them. Compared exactly (case-sensitive).</summary>
public sealed class SeedSourceRef
{
    public string? SourceSystem { get; set; }
    public string? SourceInstance { get; set; }
    public string ObjectKind { get; set; } = "";
    public string SourceRecordId { get; set; } = "";
}

public sealed class SeedFirmRelationship
{
    public string ProductName { get; set; } = "";

    /// <summary>Empty = "undecomposed" on a legacy edge; on a source-bound edge empty keeps what is
    /// stored (clear it with <c>clearFields: ["servedProcessIds"]</c>).</summary>
    public List<string> ServedProcessNames { get; set; } = [];

    /// <summary>Null = keep what is stored on a source-bound edge; 1.0 on a new or legacy one.</summary>
    public double? PriceLevel { get; set; }

    public string? AcquisitionDate { get; set; }

    /// <summary>Ends the relationship on that date. Omitting it never ends (or reopens) anything.</summary>
    public string? EndDate { get; set; }

    /// <summary>Source records this one edge stands for. Several = a deliberate consolidation.</summary>
    public List<SeedSourceRef> Sources { get; set; } = [];

    /// <summary>Source-bound edges only: acquisitionDate | endDate | pricePaidPerPeriod | servedProcessIds.</summary>
    public List<string> ClearFields { get; set; } = [];
}

public sealed class SeedFirm
{
    /// <summary>The reviewed display name. Unique per market; two source accounts with the same
    /// source name need two distinct names here.</summary>
    public string Name { get; set; } = "";
    public string? SegmentName { get; set; }

    /// <summary>Null = Observed on a new or legacy firm, keep what is stored on a source-bound one.</summary>
    public string? EpistemicStatus { get; set; }
    public string? AttributesJson { get; set; }
    public string? Notes { get; set; }
    public List<SeedFirmRelationship> Relationships { get; set; } = [];

    /// <summary>Source identity (PRD6). With it the firm is matched by its binding, not its name.</summary>
    public SeedSourceRef? Source { get; set; }

    /// <summary>Source-bound firms only: clientSegmentId | attributesJson | notes.</summary>
    public List<string> ClearFields { get; set; } = [];
}

/// <summary>
/// One historical, already-decided opportunity — calibration data, loaded via the real
/// open/qualify/decide lifecycle so the API appends the same OpportunityDecided fact a live user's
/// decision would. <c>RecordedDate</c>, when given, is passed to the decide call as
/// <c>recordedAt</c> (the bi-temporal backdating path — see <c>DecideOpportunityRequest</c>) so this
/// fact is honestly known-as-of that date rather than "now"; omitted, the server stamps now instead.
/// Backdating only — a future RecordedDate would be refused by the API.
/// </summary>
public sealed class SeedOpportunity
{
    /// <summary>Give this or <see cref="FirmSource"/>.</summary>
    public string FirmName { get; set; } = "";

    /// <summary>The firm's source identity: use it when names are ambiguous or may be renamed.</summary>
    public SeedSourceRef? FirmSource { get; set; }

    /// <summary>Source identity (PRD6): the opportunity is matched by it, advanced through
    /// qualify/decide as the file says, and never duplicated.</summary>
    public SeedSourceRef? Source { get; set; }
    public string? ChannelName { get; set; }
    public string OpenedDate { get; set; } = "";
    public string? QualifiedDate { get; set; }

    /// <summary>Null means the opportunity is still live (Open, or Qualified if QualifiedDate is
    /// set) and must not be pushed through the API's decide call.</summary>
    public string? DecidedDate { get; set; }

    /// <summary>Won | Lost | NoDecision, from our perspective (a competitor winning is "Lost").
    /// Null iff DecidedDate is null (undecided/open pipeline entry).</summary>
    public string? Outcome { get; set; }
    public List<string> ScopeProcessNames { get; set; } = [];
    public string? WinningProductName { get; set; }
    public string? CrossSellModuleProductName { get; set; }
    public string? Notes { get; set; }
    public string? RecordedDate { get; set; }

    /// <summary>True for the handful deliberately recorded a month or more after DecidedDate — see
    /// the class remarks. Purely descriptive; SeedFileTests checks it against RecordedDate.</summary>
    public bool LateRecorded { get; set; }
}

public sealed class SeedChurnEvent
{
    public string SegmentName { get; set; } = "";
    public string EffectiveDate { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>Source event identity (PRD6): two churns on the same day stay two facts.</summary>
    public SeedSourceRef? Source { get; set; }

    /// <summary>Optional affected customer, written to the payload as <c>firmId</c>.</summary>
    public string? FirmName { get; set; }
    public SeedSourceRef? FirmSource { get; set; }

    /// <summary>Optional product churned from, written to the payload as <c>productId</c>.</summary>
    public string? ProductName { get; set; }

    /// <summary>Provenance only; calibration ignores it. Defaults to <c>firmSource.sourceRecordId</c>.</summary>
    public string? SourceCustomerRef { get; set; }
    public string? SourceRelationshipRef { get; set; }

    /// <summary>Passed through as the fact's recordedAt — see <see cref="SeedOpportunity.RecordedDate"/>.</summary>
    public string? RecordedDate { get; set; }
}

public sealed class SeedArrivalCount
{
    public string SegmentName { get; set; } = "";
    public string FlowKind { get; set; } = "NewFirmArrival";

    /// <summary>The observed month, as yyyy-MM-dd (any day in the month; the loader normalizes to the 1st).</summary>
    public string Month { get; set; } = "";
    public int Count { get; set; }
    public double Exposure { get; set; } = 1.0;

    /// <summary>Source identity (PRD6); without it the row matches on (segment, month, flowKind).</summary>
    public SeedSourceRef? Source { get; set; }

    /// <summary>Passed through as the fact's recordedAt — see <see cref="SeedOpportunity.RecordedDate"/>.</summary>
    public string? RecordedDate { get; set; }
}
