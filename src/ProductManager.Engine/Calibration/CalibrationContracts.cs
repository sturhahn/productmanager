namespace ProductManager.Engine.Calibration;

// Contracts for the calibration module: prior AssumptionSet parameters + typed observations
// mapped from the facts ledger -> fitted posteriors. Pure data, JSON-serialization-friendly,
// mirroring the style of Contracts.cs / FirmLevelContracts.cs.
//
// BI-TEMPORALITY IS HONORED UPSTREAM. The facts ledger is bi-temporal (effectiveDate +
// recordedDate); the API layer filters facts by recordedDate <= asOfDate BEFORE mapping them
// into these observation records, and uses effectiveDate for ordering/windowing. This module
// therefore receives only "what we knew as of asOfDate"; AsOfDate is carried purely for
// rationale text and for clipping open-ended relationship spans. Covariate observations are
// deliberately NOT part of this contract: covariate history already reaches the engines as
// resolved covariate paths via the existing simulation input pipeline, and no engine
// parameter is fitted from them here.

/// <summary>
/// Decision result of a historical opportunity, from OUR firm's perspective (the API maps
/// won/lost relative to our products before building the observation).
/// </summary>
public enum OpportunityResult
{
    /// <summary>We won the opportunity.</summary>
    Won,

    /// <summary>A decision was made and we lost (to a competitor or an internal build).</summary>
    Lost,

    /// <summary>The firm decided not to decide. A real outcome, but not a vendor choice: it is
    /// excluded from the win-rate denominator (win rate parameterizes P(win | a decision was
    /// made)) while still contributing a duration to the sales-cycle fit.</summary>
    NoDecision,
}

/// <summary>
/// Provenance of a parameter. Mirrors the domain's ParameterProvenance by member name (the
/// engine stays dependency-free; enums serialize by name so the wire values line up).
/// </summary>
public enum CalibrationProvenance
{
    Asserted,
    Calibrated,
    Default,
}

/// <summary>
/// One historical opportunity mapped from the facts ledger. <see cref="Outcome"/> null means
/// still open as of the calibration date — excluded from every fit. Cross-sell opportunities
/// (scoped to new processes at an existing client) carry <see cref="IsCrossSell"/> and the
/// module they were selling; they feed CrossSellRate instead of WinRate.
/// </summary>
public sealed record OpportunityOutcome(
    string SegmentId,
    string? ChannelId,
    DateOnly OpenedDate,
    DateOnly? DecidedDate,
    OpportunityResult? Outcome,
    IReadOnlyList<string> ScopeProcessIds,
    string? WinnerProductId,
    bool IsCrossSell = false,
    string? ModuleId = null);

/// <summary>One churn event (a client relationship ended) mapped from the facts ledger.</summary>
public sealed record ChurnEvent(string SegmentId, DateOnly EffectiveDate);

/// <summary>
/// One monthly count observation for an arrival-type flow. <see cref="FlowKind"/> must be
/// <see cref="FlowKind.NewFirmArrival"/> (Exposure = 1 per observed month; the parameter is an
/// absolute monthly intensity) or <see cref="FlowKind.ConversionToMarket"/> (Exposure = the
/// prospect-pool size that month; the parameter is a per-prospect monthly rate and
/// count ~ Poisson(rate * pool)).
/// </summary>
public sealed record ArrivalCount(
    string SegmentId,
    FlowKind FlowKind,
    DateOnly Month,
    int Count,
    double Exposure = 1.0);

/// <summary>
/// One client-relationship span, used to compute client-month exposure for ChurnHazard and
/// CrossSellRate denominators. Null EndDate = still active (clipped at AsOfDate).
/// </summary>
public sealed record RelationshipSpan(string SegmentId, DateOnly StartDate, DateOnly? EndDate);

/// <summary>
/// A prior engine flow parameter, keyed exactly like <see cref="ParameterSpec"/>:
/// (SegmentId, FlowKind, ModuleId?), ModuleId only for CrossSellRate.
/// </summary>
public sealed record FlowPrior(
    string SegmentId,
    FlowKind FlowKind,
    string? ModuleId,
    DistributionSpec Distribution,
    CalibrationProvenance Provenance,
    string? Rationale = null);

/// <summary>
/// A prior sales-cycle spec, keyed like <see cref="SalesCycleSpec"/> (SegmentId, ProductId?).
/// Mu/Sigma are lognormal duration parameters in months. <see cref="PriorWeight"/> is the
/// prior's strength in pseudo-observations for the moment blend.
/// </summary>
public sealed record SalesCyclePrior(
    string SegmentId,
    string? ProductId,
    double Mu,
    double Sigma,
    double PriorWeight = 10.0);

/// <summary>
/// Complete input of one calibration run: prior parameters + observations already filtered by
/// recordedDate &lt;= AsOfDate upstream (see file header on bi-temporality).
/// </summary>
public sealed record CalibrationInput(
    DateOnly AsOfDate,
    IReadOnlyList<FlowPrior> FlowPriors,
    IReadOnlyList<SalesCyclePrior> SalesCyclePriors,
    IReadOnlyList<OpportunityOutcome> Opportunities,
    IReadOnlyList<ChurnEvent> ChurnEvents,
    IReadOnlyList<ArrivalCount> ArrivalCounts,
    IReadOnlyList<RelationshipSpan> RelationshipSpans);

/// <summary>Prior-vs-posterior comparison for one fitted parameter. Shift = posterior mean - prior mean.</summary>
public sealed record ParameterDiff(
    double PriorMean,
    double PriorStdDev,
    double PosteriorMean,
    double PosteriorStdDev,
    double Shift);

/// <summary>
/// One fitted flow parameter (same keying as the engine's <see cref="ParameterSpec"/>).
/// Probability flows (WinRate, ChurnHazard, CrossSellRate) report a Beta posterior; rate flows
/// (NewFirmArrival, ConversionToMarket) have a Gamma posterior, reported moment-matched as a
/// LogNormal (positive support; DistributionSpec has no Gamma family).
/// </summary>
public sealed record FittedFlowParameter(
    string SegmentId,
    FlowKind FlowKind,
    string? ModuleId,
    DistributionSpec Posterior,
    CalibrationProvenance Provenance,
    string AutoRationale,
    int SampleSize,
    ParameterDiff Diff);

/// <summary>A flow prior that could not be fitted, passed through unchanged with the reason.</summary>
public sealed record UnfittedFlowParameter(
    string SegmentId,
    FlowKind FlowKind,
    string? ModuleId,
    DistributionSpec Distribution,
    CalibrationProvenance Provenance,
    string Reason);

/// <summary>One fitted sales-cycle spec (same keying as <see cref="SalesCycleSpec"/>).</summary>
public sealed record FittedSalesCycle(
    string SegmentId,
    string? ProductId,
    double Mu,
    double Sigma,
    CalibrationProvenance Provenance,
    string AutoRationale,
    int SampleSize,
    ParameterDiff Diff);

/// <summary>A sales-cycle prior that could not be fitted, passed through unchanged with the reason.</summary>
public sealed record UnfittedSalesCycle(
    string SegmentId,
    string? ProductId,
    double Mu,
    double Sigma,
    string Reason);

/// <summary>
/// Result of one calibration run. Every prior appears exactly once, in input order, in either
/// the fitted or the unfitted list. Deterministic: no RNG anywhere, so identical inputs produce
/// byte-identical outputs.
/// </summary>
public sealed record CalibrationOutput(
    DateOnly AsOfDate,
    IReadOnlyList<FittedFlowParameter> FittedFlows,
    IReadOnlyList<UnfittedFlowParameter> UnfittedFlows,
    IReadOnlyList<FittedSalesCycle> FittedSalesCycles,
    IReadOnlyList<UnfittedSalesCycle> UnfittedSalesCycles);
