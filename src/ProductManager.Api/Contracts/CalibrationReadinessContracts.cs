using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ProductManager.Domain.Common;
using ProductManager.Domain.Simulation;

namespace ProductManager.Api.Contracts;

// PRD8: prior evidence, the evidence-reuse refusal, and the calibration readiness report.

/// <summary>
/// Which observations informed a prior. Omitted (null) on a parameter means <em>unknown</em>: a fit
/// still runs, and its readiness report carries an unresolved <c>PriorEvidenceUnknown</c> issue.
/// <list type="bullet">
/// <item><c>Independent</c>: not derived from ledger observations (flat Beta(1,1), a benchmark). No dates.</item>
/// <item><c>Observations</c> + <c>observedFrom</c>/<c>observedThrough</c>: derived from outcomes effective in
/// that inclusive period (e.g. a <c>Beta(wins+1, losses+1)</c> from last year's deals). A fit is refused while
/// any of its observations for the parameter is effective inside the period.</item>
/// <item><c>Observations</c> + <c>recordedThrough</c>: derived from the ledger as recorded through that date.
/// A fit stamps this (with <c>sourceCalibrationRunId</c>) on every parameter it moves; a later fit is refused
/// unless it reads only facts recorded after that date (<c>observationsRecordedAfter</c> &gt;= it).</item>
/// </list>
/// A stamp keeps the period of the prior it updated verbatim (none when the chain started Independent or unknown),
/// and both rules apply to a stamp that has both.
/// </summary>
public sealed record PriorEvidenceDto(
    PriorEvidenceBasis Basis,
    DateOnly? ObservedFrom = null,
    DateOnly? ObservedThrough = null,
    DateOnly? RecordedThrough = null,
    [MaxLength(500)] string? Population = null,
    Guid? SourceCalibrationRunId = null)
{
    public PriorEvidence ToDomain() =>
        new(Basis, ObservedFrom, ObservedThrough, RecordedThrough, Population, SourceCalibrationRunId);

    public static PriorEvidenceDto? From(PriorEvidence? e) => e is null
        ? null
        : new(e.Basis, e.ObservedFrom, e.ObservedThrough, e.RecordedThrough, e.Population, e.SourceCalibrationRunId);
}

/// <summary>
/// 409 with <c>type</c> <see cref="ProblemTypes.PriorEvidenceReuse"/>: at least one prior the run would
/// fit was informed by observations the fit would apply again. Nothing was fitted or stored.
/// </summary>
public sealed class PriorEvidenceReuseProblem : ProblemDetails
{
    public IReadOnlyList<PriorEvidenceOverlapDto> Overlaps { get; init; } = [];
}

/// <summary>Which rule of the evidence-reuse check found an overlap.</summary>
public enum PriorEvidenceRule
{
    /// <summary>The prior's observation period: fit observations (or client-months) effective inside it.</summary>
    ObservedPeriod,

    /// <summary>The prior's recorded cut-off: fit observations recorded on or before it, exposure through it.</summary>
    RecordedThrough
}

/// <summary>
/// One refused parameter and rule (a prior carrying both a period and a cut-off can appear twice): its prior evidence, the effective-date range of the overlapping observations,
/// how many of the fit's observations for it overlap (and, for exposure-denominated flows, the
/// overlapping client-months), out of how many the fit would use.
/// </summary>
public sealed record PriorEvidenceOverlapDto(
    string Key,
    SimulationFlowKind? FlowKind,
    Guid? ClientSegmentId,
    Guid? ModuleProductId,
    Guid? ProductId,
    PriorEvidenceDto PriorEvidence,
    DateOnly? OverlapFrom,
    DateOnly? OverlapThrough,
    int OverlappingObservations,
    double? OverlappingExposure,
    int FitObservations,
    PriorEvidenceRule Rule = PriorEvidenceRule.ObservedPeriod);

/// <summary>Readiness of one parameter. Closed set.</summary>
public enum CalibrationReadinessStatus
{
    /// <summary>Fitted, and no readiness issue is open.</summary>
    Ready,

    /// <summary>Fitted, but at least one issue is open (see <c>issues</c>).</summary>
    NotReady,

    /// <summary>The engine did not fit it (see <c>unfittedReason</c>); the prior stands unchanged.</summary>
    Unfitted,

    /// <summary>A quantity calibration does not and cannot fit (choice model, reference data): an assumption.</summary>
    Asserted
}

/// <summary>Run-level verdict. Closed set; never "validated".</summary>
public enum CalibrationReadinessOverall
{
    /// <summary>
    /// Every fit parameter is Ready and nothing asserted is in play. Because every choice-model quantity the
    /// set does not define is listed as asserted with its default, a report with a choice model is Exploratory.
    /// </summary>
    Ready,

    /// <summary>Anything else: usable for exploratory simulation with the listed assumptions, not a validated forecast.</summary>
    Exploratory
}

/// <summary>Why a parameter is not Ready. Closed set; every code is an open issue.</summary>
public enum ReadinessIssueCode
{
    /// <summary>The prior carries no evidence metadata, so overlap with the fit's observations cannot be ruled out.</summary>
    PriorEvidenceUnknown,

    /// <summary>Relationships with our products in the segment have no acquisition date and contribute no exposure.</summary>
    MissingAcquisitionDate,

    /// <summary>More churn events than ended relationships in the segment: an end date was probably omitted.</summary>
    ChurnWithoutEndDate,

    /// <summary>Churn facts name no product while firms hold more than one of our products: logo-level events against relationship-level exposure.</summary>
    LogoLevelChurn,

    /// <summary>Firms or facts without a segment were left out of every segment's fit.</summary>
    UnsegmentedEvidence,

    /// <summary>No relationship spans: the engine approximated churn exposure (churns x window / 2).</summary>
    ExposureApproximated,

    /// <summary>Months inside the observed arrival window have no observation (missing, not zero).</summary>
    ArrivalMonthsMissing,

    /// <summary>A month was observed more than once; each counts as a separate month of exposure.</summary>
    DuplicateArrivalMonth,

    /// <summary>Facts of a kind this parameter reads were recorded on or before the run's cut-off but entered after the
    /// run was stored (a backdated import): the stored snapshot no longer matches the ledger. Refit.</summary>
    LedgerChangedSinceRun
}

/// <summary>Held-out comparison outcome for one parameter. Closed set; there is no "pass".</summary>
public enum HoldoutComparisonStatus
{
    /// <summary>Held-out outcomes exist and were compared with the posterior predictive interval.</summary>
    Compared,

    /// <summary>No comparison: no held-out outcomes, or the inputs would need history we do not have (see <c>reason</c>).</summary>
    Unsupported
}

public sealed record ReadinessIssueDto(ReadinessIssueCode Code, string Message);

/// <summary>
/// Readiness of one parameter the run could fit (flow or sales cycle), in one segment.
/// <c>observations</c> is the denominator count in the parameter's own units (decided opportunities,
/// churn events, observed months); <c>successes</c>/<c>failures</c> are wins/losses, churns, cross-sell
/// wins or arrivals; <c>excluded</c> counts observations deliberately left out of the denominator
/// (no-decision outcomes for WinRate); <c>exposure</c> is client-months (ChurnHazard, CrossSellRate) or
/// exposure-months (arrivals). <c>observedFrom</c>/<c>observedThrough</c> are the effective dates actually used.
/// </summary>
public sealed record ReadinessParameterDto(
    string Key,
    SimulationFlowKind? FlowKind,
    Guid? ClientSegmentId,
    Guid? ModuleProductId,
    Guid? ProductId,
    CalibrationReadinessStatus Status,
    string Units,
    string Denominator,
    DateOnly? ObservedFrom,
    DateOnly? ObservedThrough,
    int Observations,
    int? Successes,
    int? Failures,
    int? Excluded,
    double? Exposure,
    DistributionSpecDto? Prior,
    ParameterProvenance? PriorProvenance,
    PriorEvidenceDto? PriorEvidence,
    DistributionSpecDto? Posterior,
    double? PosteriorMean,
    string? UnfittedReason,
    IReadOnlyList<ReadinessIssueDto> Issues);

/// <summary>
/// A quantity the run did not and cannot fit — choice-model parameters in the prior set, the choice-model
/// quantities the set does not define (provenance <c>Default</c>, the firm-level engine's documented default
/// value), and choice inputs held as reference data (<c>reference:*</c> keys, with <c>count</c> rows). Win
/// rates and cycle times do not validate these.
/// </summary>
public sealed record ReadinessAssertedDto(
    string Key,
    string Quantity,
    CalibrationReadinessStatus Status,
    ParameterProvenance? Provenance,
    DistributionSpecDto? Distribution,
    string? Rationale,
    int Count);

public sealed record ReadinessModuleHoldersDto(Guid ModuleProductId, string? Name, int Holders);

/// <summary>Decisions the fit read, by result, split into new business and cross-sell.</summary>
public sealed record ReadinessDecisionCountsDto(
    int Won, int Lost, int NoDecision, int CrossSellWon, int CrossSellLost, int CrossSellNoDecision);

/// <summary>
/// System-side population numbers for one segment at the run's as-of date, for reconciling against
/// sources. Firm counts are current state; active customers and module holders are distinct firms with a
/// relationship of ours active at the as-of date (not ended by it, and acquired on or before it or with no
/// acquisition date — those also count in <c>relationshipsWithoutAcquisitionDate</c>); relationship counts are
/// every relationship of ours held by the segment's firms. <c>clientMonths</c> is the exposure the fit
/// used (base products and modules alike, one per relationship).
/// </summary>
public sealed record ReadinessSegmentPopulationDto(
    Guid ClientSegmentId,
    string? SegmentName,
    int ObservedFirms,
    int InferredFirms,
    int SyntheticFirms,
    int ActiveCustomers,
    IReadOnlyList<ReadinessModuleHoldersDto> ModuleHolders,
    ReadinessDecisionCountsDto Decisions,
    int ChurnEvents,
    int ProductlessChurnEvents,
    int Relationships,
    int RelationshipsWithoutAcquisitionDate,
    int EndedRelationships,
    int MultiProductFirms,
    double ClientMonths);

/// <summary>
/// Ledger facts of one calibration kind that the fit did not read, counted now: recorded after the
/// as-of cut-off though effective on or before it; recorded on or before the observation window's
/// start; read but not attributable (no segment, unreadable payload); and recorded on or before the
/// as-of date but entered after the run was stored (a later backdated import the run never saw).
/// </summary>
public sealed record ReadinessExclusionDto(
    FactEventKind Kind,
    int RecordedAfterAsOf,
    int RecordedOnOrBeforeWindowStart,
    int Unattributed,
    int AddedAfterRun);

/// <summary>
/// Monthly arrival observations of one segment and flow: every observed month (zeros included — an
/// observed zero is evidence), and the months between the first and last observation that have none
/// (missing, never treated as zero). The gap check stops at the last observation: months after it up to
/// the as-of date are neither listed nor counted.
/// </summary>
public sealed record ReadinessArrivalSeriesDto(
    Guid? ClientSegmentId,
    SimulationFlowKind FlowKind,
    int ObservedMonths,
    int ZeroMonths,
    DateOnly? FirstMonth,
    DateOnly? LastMonth,
    double Exposure,
    IReadOnlyList<DateOnly> MissingMonths,
    IReadOnlyList<DateOnly> DuplicateMonths);

/// <summary>
/// One fitted parameter against outcomes effective after the as-of date. <c>sampleSize</c> is decided
/// deals (WinRate) or observed months (arrivals); <c>observedEvents</c> wins or arrivals;
/// <c>intervalLower</c>/<c>intervalUpper</c> the central posterior-predictive interval of the held-out
/// rate given that sample size. Inside or outside the interval is a discrepancy statement, not a pass.
/// </summary>
public sealed record ReadinessHoldoutComparisonDto(
    string Key,
    SimulationFlowKind? FlowKind,
    Guid? ClientSegmentId,
    Guid? ModuleProductId,
    Guid? ProductId,
    HoldoutComparisonStatus Status,
    string? Reason,
    int SampleSize,
    int? ObservedEvents,
    double? Exposure,
    double? ObservedRate,
    double? PosteriorMean,
    double? IntervalLower,
    double? IntervalUpper,
    bool? ObservedInsideInterval);

public sealed record ReadinessHoldoutDto(
    DateOnly HeldOutFrom,
    DateOnly HeldOutThrough,
    double IntervalMass,
    string Note,
    IReadOnlyList<ReadinessHoldoutComparisonDto> Comparisons);

/// <summary>
/// The readiness report of one calibration run, computed on read from the run's stored input, output
/// and prior-parameter snapshots plus current reference data and ledger counts. It never refits and
/// never edits the run. Readiness is about evidence completeness and reuse, not forecast accuracy.
/// </summary>
public sealed record CalibrationReadinessDto(
    Guid CalibrationRunId,
    DateOnly AsOfDate,
    DateOnly? ObservationsRecordedAfter,
    Guid PriorAssumptionSetId,
    Guid? FittedAssumptionSetId,
    CalibrationReadinessOverall Overall,
    IReadOnlyList<string> OverallReasons,
    IReadOnlyList<ReadinessParameterDto> Parameters,
    IReadOnlyList<ReadinessAssertedDto> Asserted,
    IReadOnlyList<ReadinessSegmentPopulationDto> Populations,
    IReadOnlyList<ReadinessExclusionDto> Exclusions,
    IReadOnlyList<ReadinessArrivalSeriesDto> ArrivalObservations,
    IReadOnlyList<string> Notes,
    ReadinessHoldoutDto? Holdout);
