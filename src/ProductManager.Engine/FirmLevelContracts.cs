namespace ProductManager.Engine;

// ---------------------------------------------------------------------------------------------
// Engine v1 — firm-level choice engine contracts. Purely additive alongside the v0 aggregate
// contracts in Contracts.cs; shared primitives (DistributionSpec, CovariateLink, ParameterSpec,
// CovariateInput, EventInput, CostRates, trajectory records) are reused, not duplicated.
// All records are sealed, JSON-serialization-friendly pure data.
// ---------------------------------------------------------------------------------------------

/// <summary>One business process in the market's catalog. Processes sharing a
/// <see cref="ClusterId"/> tend to be bought together; a null cluster means the process forms
/// its own singleton cluster. Clusters drive opportunity scoping and land-and-expand adjacency.
/// The engine supports at most 64 processes per run (scopes are tracked as bitmasks).</summary>
public sealed record ProcessInput(string Id, string? ClusterId = null);

/// <summary>Share of a segment's firms using an approach (e.g. 99% DTCC / 1% chain). Shares are
/// normalized by the engine; an empty mix means one implicit approach (null approachId).</summary>
public sealed record ApproachShare(string ApproachId, double Share);

/// <summary>
/// A segment's demand profile for one business process: the distribution firm importance weights
/// are sampled from (for firms without observed weights), plus constraint semantics.
/// If <see cref="RequiredCoverage"/> &gt; 0 and a candidate's effective coverage falls below it:
/// a hard need disqualifies the candidate from the consideration set; a soft need instead adds
/// <see cref="WorkaroundCost"/> (scaled by the global workaroundCostWeight) to its utility penalty.
/// </summary>
public sealed record ProcessNeed(
    string ProcessId,
    DistributionSpec ImportanceWeight,
    bool IsHard = false,
    double RequiredCoverage = 0.0,
    double WorkaroundCost = 0.0);

/// <summary>Synthetic-fill target: the segment should start with this many client firms holding
/// this product. Roster firms holding the product count toward it; the gap is filled with
/// deterministically generated synthetic client firms (modules = every process the product
/// covers in the segment, list price).</summary>
public sealed record InitialClientTarget(string ProductId, int Count);

/// <summary>
/// One client segment for the firm-level engine. <see cref="InitialProspectPool"/> is a stock
/// target: synthetic prospect firms are generated to fill the gap between roster non-client
/// firms and this number. <see cref="PriceSensitivity"/> and
/// <see cref="ConsolidationPreference"/> are segment-level defaults for firms that don't carry
/// their own (latent attributes sampled per MC path).
/// </summary>
public sealed record FirmLevelSegmentInput(
    string Id,
    double InitialProspectPool,
    IReadOnlyList<InitialClientTarget> InitialClientTargets,
    IReadOnlyList<ApproachShare> ApproachMix,
    IReadOnlyList<ProcessNeed> ProcessNeeds,
    DistributionSpec PriceSensitivity,
    double ConsolidationPreference,
    CostRates CostRates);

/// <summary>An existing firm↔product relationship: which processes ("modules") the product
/// currently serves at the firm, and the price level actually paid as a multiplier on list
/// price (1.0 = list). Revenue for our relationships uses the price level; utilities use list
/// price for all candidates.</summary>
public sealed record IncumbentRelationship(
    string ProductId,
    IReadOnlyList<string> ProcessIds,
    double PriceLevel = 1.0);

/// <summary>An observed importance weight for one process at one firm.</summary>
public sealed record FirmImportance(string ProcessId, double Weight);

/// <summary>
/// One firm in the roster. Null <see cref="ImportanceWeights"/> / <see cref="PriceSensitivity"/>
/// / <see cref="ConsolidationPreference"/> mean "latent": sampled per MC path from the segment's
/// distributions (importance per process from ProcessNeeds, price sensitivity from the segment
/// spec) or taken from the segment default (consolidation preference).
/// </summary>
public sealed record FirmInput(
    string Id,
    string SegmentId,
    bool IsSynthetic,
    IReadOnlyList<IncumbentRelationship> Relationships,
    IReadOnlyList<FirmImportance>? ImportanceWeights = null,
    DistributionSpec? PriceSensitivity = null,
    double? ConsolidationPreference = null);

/// <summary>
/// Coverage of one (process, approach?, segment?) by a product as a numeric score (tier mapping
/// is the caller's job; 0 = not playing). A null <see cref="ApproachId"/> is the default cell
/// applying to every approach without a specific cell. A null <see cref="SegmentId"/> is the
/// generic cell applying to every segment; a segment-specific cell shadows the generic cell with
/// the same (process, approach) key for that segment only and is NEVER applied to firms in other
/// segments. Resolution precedence per (process, approach) is exact-then-generic on both axes,
/// approach first: (approach, segment) &gt; (approach, generic) &gt; (default approach, segment)
/// &gt; (default approach, generic); no matching cell means no coverage. Shadowing applies to
/// BASE scores only — <see cref="CoverageStep"/>s resolve independently and combine as
/// max(base, step), so a scoped base cell never cancels a generic cell's steps. <see cref="HasCapacity"/>
/// false means the product cannot onboard NEW clients for this process (in the cell's segment, or
/// everywhere for a generic cell): candidates whose scope touches it are dropped from the
/// consideration set unless they already serve part of the scope at the firm.
/// </summary>
public sealed record CoverageCell(
    string ProcessId,
    string? ApproachId,
    double Score,
    bool HasCapacity = true,
    string? SegmentId = null);

/// <summary>
/// Time-phased coverage: from <see cref="Month"/> (0-based simulation month) onward, the
/// (process, approach?, segment?) cell carries step score <see cref="Score"/>; multiple steps
/// for the same cell are applied in month order (ties: later list entries win).
/// <para>
/// <b>Resolution rule — max(base, step):</b> the effective score for a firm's (segment,
/// process, approach, month t) is <c>max(resolvedBase, resolvedStep(t))</c>, where the base
/// <see cref="CoverageCell"/>s and the latest applicable step (step month &lt;= t) each resolve
/// INDEPENDENTLY through the same precedence chain — (approach, segment) &gt;
/// (approach, generic) &gt; (default, segment) &gt; (default, generic). Consequences: a
/// segment-scoped base cell shadows the generic BASE score but does NOT cancel a more generic
/// cell's steps (roadmap lifts still apply in segments that carry their own assessment); a
/// scoped step still takes precedence over a generic step for its segment; and steps never
/// LOWER effective coverage — a resolved step below the resolved base is ignored. A step with
/// no base cell anywhere in the chain simply brings coverage into existence at
/// <see cref="Month"/>.
/// </para>
/// Coverage is evaluated as of the opportunity's OPEN month (utilities are drawn at open), so a
/// step at month m affects only opportunities opened at t &gt;= m — including hard-constraint
/// qualification and capacity-independent coverage masks, which all use the same effective
/// score. Steps never change <see cref="CoverageCell.HasCapacity"/>.
/// </summary>
public sealed record CoverageStep(
    string ProcessId,
    string? ApproachId,
    string? SegmentId,
    int Month,
    double Score);

/// <summary>Per-segment pricing: recurring price per covered in-scope process per month, plus a
/// one-time implementation fee per new relationship/extension. A product with no price entry
/// for a segment is not sellable there (excluded from consideration).</summary>
public sealed record ProductPrice(string SegmentId, double ArpuPerProcessPerMonth, double ImplementationFee);

/// <summary>Integration quality edge to another product (symmetric; the engine reads the max of
/// the two directions if both are given). Enters best-of-breed utility against a firm's stack.</summary>
public sealed record IntegrationEdge(string OtherProductId, double Quality);

/// <summary>Additive brand/reputation utility bias of a product within a segment.</summary>
public sealed record BrandBias(string SegmentId, double Bias);

/// <summary>A sellable product, ours or a competitor's. <see cref="CoverageSteps"/> (optional)
/// time-phases the coverage grid: each step rewrites one cell's score from its month onward
/// (see <see cref="CoverageStep"/>); null/empty means coverage is constant over the horizon.</summary>
public sealed record FirmLevelProductInput(
    string Id,
    bool IsOurs,
    IReadOnlyList<CoverageCell> Coverage,
    IReadOnlyList<ProductPrice> Pricing,
    IReadOnlyList<IntegrationEdge> Integrations,
    IReadOnlyList<BrandBias> Brand,
    IReadOnlyList<CoverageStep>? CoverageSteps = null);

/// <summary>A (segmentId, value) pair used for channel reach and origination shares.</summary>
public sealed record SegmentShare(string SegmentId, double Value);

/// <summary>Additive utility bias a channel applies to a product it presents.</summary>
public sealed record ChannelProductBias(string ProductId, double Bias);

/// <summary>
/// A route to market. An opportunity's channel is drawn per segment with weight
/// reach × originationShare per listed channel — NOT renormalized when the weights sum to less
/// than 1: the residual 1 − Σ(reach × originationShare) (clamped ≥ 0) originates through the
/// implicit unbiased direct channel (no product bias, full product visibility, reported as the
/// "direct" funnel row). If the weights sum to more than 1 they are normalized down
/// proportionally and no direct residual remains; a sum of exactly 1 leaves zero direct. A
/// channel with a non-null <see cref="CarriesProducts"/> list only presents those products:
/// others are excluded from the consideration set of opportunities it originates (the "channel
/// reach" filter).
/// </summary>
public sealed record ChannelInput(
    string Id,
    IReadOnlyList<SegmentShare> SegmentReach,
    IReadOnlyList<SegmentShare> OriginationShare,
    IReadOnlyList<ChannelProductBias> ProductBias,
    IReadOnlyList<string>? CarriesProducts = null);

/// <summary>
/// Sales-cycle length in months ~ LogNormal(Mu, Sigma) keyed by segment, with optional
/// per-product override (applied when that product is the opportunity's chosen option). With no
/// matching spec the decision is instant (v0-style, delay 0).
/// </summary>
public sealed record SalesCycleSpec(string SegmentId, string? ProductId, double Mu, double Sigma);

/// <summary>
/// Global choice-model parameters.
/// <list type="bullet">
/// <item><see cref="SwitchingCostBase"/>: utility penalty on a non-incumbent candidate, scaled by
/// the fraction of the scope currently served by other products at the firm.</item>
/// <item><see cref="WorkaroundCostWeight"/>: multiplier on soft-need workaround costs.</item>
/// <item><see cref="ExpansionPropensity"/>: scales the probability of one mid-cycle scope
/// expansion, multiplied by the winner's coverage advantage on the best adjacent cluster.</item>
/// <item><see cref="NoDecisionUtility"/>: utility of the outside option. For new-business and
/// cross-sell opportunities it means "no decision"; for incumbent re-evaluations it means
/// dropping the provider without replacement (churn).</item>
/// <item><see cref="IntegrationWeight"/>: multiplier on the mean integration quality between a
/// candidate and the firm's existing stack.</item>
/// <item><see cref="PriceScale"/>: divisor normalizing the monthly scope price before it is
/// multiplied by the firm's price sensitivity (utility -= sens × price / priceScale).</item>
/// </list>
/// </summary>
public sealed record ChoiceParameters(
    double SwitchingCostBase,
    double WorkaroundCostWeight,
    double ExpansionPropensity,
    IReadOnlyList<SalesCycleSpec> SalesCycles,
    double NoDecisionUtility,
    double IntegrationWeight = 1.0,
    double PriceScale = 1000.0);

/// <summary>
/// How a segment scopes opportunities. With probability <see cref="BroadScopeProbability"/> the
/// scope is every process (suite purchase); otherwise the scope covers
/// round(sample(<see cref="ScopeClusterCount"/>)) clusters (min 1) drawn uniformly. Cross-sell
/// scopes are additionally restricted to processes the firm doesn't already have covered.
/// A segment without a scoping entry always scopes broadly.
/// </summary>
public sealed record OpportunityScoping(
    string SegmentId,
    double BroadScopeProbability,
    DistributionSpec ScopeClusterCount);

/// <summary>
/// Complete, self-contained input of one firm-level simulation run.
/// <para>
/// Reused v0 primitives: <see cref="ParameterSpec"/> drives opportunity arrivals —
/// NewFirmArrival (synthetic firm formation), ConversionToMarket (prospect pool → new-business
/// opportunities), ChurnHazard (per-relationship re-evaluation trigger; realized churn emerges
/// from the choice model), CrossSellRate with null moduleId (per-client cross-sell probe rate).
/// WinRate parameters and per-module CrossSellRate parameters are ignored: win probability
/// emerges from utilities. Covariate elasticities and event rate multipliers apply to those
/// flows exactly as in v0 (event WinRate/attractiveness multipliers have no firm-level analog).
/// </para>
/// </summary>
public sealed record FirmLevelInput(
    IReadOnlyList<FirmLevelSegmentInput> Segments,
    IReadOnlyList<ProcessInput> Processes,
    IReadOnlyList<FirmInput> Firms,
    IReadOnlyList<FirmLevelProductInput> Products,
    IReadOnlyList<ChannelInput> Channels,
    ChoiceParameters Choice,
    IReadOnlyList<OpportunityScoping> Scoping,
    IReadOnlyList<ParameterSpec> Parameters,
    IReadOnlyList<CovariateInput> Covariates,
    IReadOnlyList<EventInput> Events,
    int HorizonMonths,
    int Paths = 500,
    int Seed = 0,
    double DiscountRateAnnual = 0.10,
    double TerminalGrowthAnnual = 0.02);

/// <summary>Opportunity funnel counts for one segment, mean across MC paths. Conservation holds
/// exactly per path: Opened == Won + Lost + NoDecision + StillOpen. "Won"/"Lost" are from our
/// perspective (an our-product choice / a competitor-product choice, including incumbent stays);
/// "Qualified" counts opportunities where at least one of our products made the consideration
/// set. WinRate = Won / Opened (0 when nothing opened).</summary>
public sealed record SegmentFunnel(
    string SegmentId,
    double Opened,
    double Qualified,
    double Won,
    double Lost,
    double NoDecision,
    double StillOpen,
    double WinRate);

/// <summary>Win funnel by channel, mean across MC paths. "direct" is the implicit channel: it
/// originates the residual 1 − Σ(reach × originationShare) share in each segment (everything, in
/// segments with no configured channel weight).</summary>
public sealed record ChannelFunnel(string ChannelId, double Opened, double Won, double WinRate);

/// <summary>
/// Additive diagnostics of one firm-level run, all mean-across-paths.
/// <see cref="MeanUtilityGapLostDeals"/> = mean over lost deals (where we had a qualified
/// candidate) of winner utility minus our best candidate's utility.
/// <see cref="SyntheticFirmsCreated"/> counts initial stock fill plus mid-run arrivals.
/// </summary>
public sealed record FirmLevelDiagnostics(
    IReadOnlyList<SegmentFunnel> FunnelBySegment,
    IReadOnlyList<ChannelFunnel> FunnelByChannel,
    double AvgSalesCycleMonths,
    double ExpansionCount,
    double MeanUtilityGapLostDeals,
    double SyntheticFirmsCreated);

/// <summary>
/// Result of one firm-level run: <see cref="Result"/> is byte-compatible with the v0
/// <see cref="EngineResult"/> trajectory format (downstream compare/UI work unchanged), and
/// <see cref="Diagnostics"/> is the additive firm-level block.
/// </summary>
public sealed record FirmLevelResult(EngineResult Result, FirmLevelDiagnostics Diagnostics);
