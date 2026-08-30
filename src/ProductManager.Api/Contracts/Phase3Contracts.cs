using System.ComponentModel.DataAnnotations;
using ProductManager.Domain.Common;
using ProductManager.Domain.Market;
using ProductManager.Domain.Simulation;

namespace ProductManager.Api.Contracts;

// ---------- Channel ----------

/// <summary>
/// One segment a channel touches: how much of it the channel can reach, and how much of its
/// opportunity flow the channel originates. Reach and origination are separate questions — a
/// consultant may see a whole segment yet originate nothing — and the choice engine draws an
/// opportunity's channel with weight reach × originationShare.
/// </summary>
public sealed record ChannelSegmentReachDto(
    Guid ClientSegmentId,
    [Range(0, 1)] double Reach,
    [Range(0, 1)] double OriginationShare);

/// <summary>Signed utility bias a channel applies to a product it presents. 0 is neutral.</summary>
public sealed record ChannelProductAffinityDto(Guid ProductId, double Bias);

public sealed record ChannelDto(
    Guid Id,
    Guid MarketId,
    string Name,
    string? Description,
    bool OriginatesOpportunities,
    bool CarriesOnlyListedProducts,
    IReadOnlyList<ChannelSegmentReachDto> SegmentReaches,
    IReadOnlyList<ChannelProductAffinityDto> ProductAffinities);

/// <summary>
/// A route to market; direct sales is just another channel.
/// <c>carriesOnlyListedProducts</c> turns the channel's affinity list into a whitelist: products it
/// does not carry drop out of the consideration set of the opportunities it originates.
/// </summary>
public sealed record CreateChannelRequest(
    [Required] Guid MarketId,
    [Required, MaxLength(200)] string Name,
    string? Description = null,
    bool OriginatesOpportunities = false,
    bool CarriesOnlyListedProducts = false);

public sealed record UpdateChannelRequest(
    [Required, MaxLength(200)] string Name,
    string? Description = null,
    bool OriginatesOpportunities = false,
    bool CarriesOnlyListedProducts = false);

public sealed record SetChannelSegmentReachRequest(
    [Required] Guid ClientSegmentId,
    [Range(0, 1)] double Reach,
    [Range(0, 1)] double OriginationShare = 0);

public sealed record SetChannelProductAffinityRequest([Required] Guid ProductId, double Bias);

// ---------- Integration ----------

public sealed record IntegrationDto(
    Guid Id,
    Guid ProductAId,
    Guid ProductBId,
    double QualityScore,
    string? Notes);

/// <summary>
/// A Product↔Product edge with a quality score in [0, 1]. The edge is symmetric: posting (A, B) and
/// (B, A) describe the same integration, and the second is rejected as a duplicate.
/// </summary>
public sealed record CreateIntegrationRequest(
    [Required] Guid ProductAId,
    [Required] Guid ProductBId,
    [Range(0, 1)] double QualityScore,
    string? Notes = null);

public sealed record UpdateIntegrationRequest([Range(0, 1)] double QualityScore, string? Notes = null);

// ---------- Firm↔Product relationship ----------

/// <summary>
/// "Being a client" as an edge: which product, which processes it actually serves at the firm
/// (the modules held), what fraction of list price is paid, and when it started and ended.
/// </summary>
public sealed record FirmProductRelationshipDto(
    Guid Id,
    Guid FirmId,
    Guid ProductId,
    IReadOnlyList<Guid> ServedProcessIds,
    double PriceLevel,
    MoneyDto? PricePaidPerPeriod,
    DateOnly? AcquisitionDate,
    DateOnly? EndDate,
    bool IsActive,
    string? Notes);

/// <summary>
/// <c>servedProcessIds</c> may be empty — that reads as "whatever the product covers", the honest
/// state of a relationship nobody has decomposed into modules yet. <c>priceLevel</c> is a
/// multiplier on list price (1.0 = list, 0.8 = a 20% discount).
/// </summary>
public sealed record CreateFirmProductRelationshipRequest(
    [Required] Guid FirmId,
    [Required] Guid ProductId,
    IReadOnlyList<Guid>? ServedProcessIds = null,
    [Range(0.0001, 1000)] double PriceLevel = 1.0,
    MoneyDto? PricePaidPerPeriod = null,
    DateOnly? AcquisitionDate = null,
    string? Notes = null);

/// <summary>
/// Replaces the whole edge. Omitting <c>endDate</c> reopens a relationship that had ended — a PUT
/// states what is true now, and "no end date" means "still a client".
/// </summary>
public sealed record UpdateFirmProductRelationshipRequest(
    IReadOnlyList<Guid>? ServedProcessIds = null,
    [Range(0.0001, 1000)] double PriceLevel = 1.0,
    MoneyDto? PricePaidPerPeriod = null,
    DateOnly? AcquisitionDate = null,
    DateOnly? EndDate = null,
    string? Notes = null);

// ---------- Firm bulk import & synthetic generation ----------

/// <summary>One firm in a bulk import, with the relationships it already holds.</summary>
public sealed record FirmImportItem(
    [Required, MaxLength(200)] string Name,
    Guid? ClientSegmentId = null,
    EpistemicStatus EpistemicStatus = EpistemicStatus.Observed,
    string? AttributesJson = null,
    string? Notes = null,
    IReadOnlyList<FirmImportRelationship>? Relationships = null);

public sealed record FirmImportRelationship(
    [Required] Guid ProductId,
    IReadOnlyList<Guid>? ServedProcessIds = null,
    [Range(0.0001, 1000)] double PriceLevel = 1.0,
    MoneyDto? PricePaidPerPeriod = null,
    DateOnly? AcquisitionDate = null);

/// <summary>
/// Imports a roster of firms into one market. Matching is by natural key (market, name), so
/// re-importing the same roster updates rather than duplicates it — the same idempotence the CLI's
/// seed loader relies on.
/// </summary>
public sealed record BulkImportFirmsRequest(
    [Required] Guid MarketId,
    [Required] IReadOnlyList<FirmImportItem> Firms);

public sealed record BulkImportFirmsResponse(
    Guid MarketId,
    int Created,
    int Updated,
    int RelationshipsWritten,
    IReadOnlyList<FirmDto> Firms);

/// <summary>
/// Fills a market's segment stocks with deterministically generated synthetic firms. Regeneration
/// replaces the previous synthetic population rather than adding a second one.
/// </summary>
public sealed record GenerateSyntheticFirmsRequest([Required] Guid MarketId);

public sealed record SyntheticSegmentFillDto(
    Guid ClientSegmentId,
    int ProspectTarget,
    int ObservedProspects,
    int SyntheticProspects,
    int ClientTarget,
    int ObservedClients,
    int SyntheticClients);

public sealed record GenerateSyntheticFirmsResponse(
    Guid MarketId,
    int Removed,
    int Created,
    int RelationshipsCreated,
    IReadOnlyList<SyntheticSegmentFillDto> Segments);

// ---------- Opportunity ----------

public sealed record OpportunityScopeItemDto(Guid BusinessProcessId, DateTimeOffset AddedAt);

public sealed record OpportunityDto(
    Guid Id,
    Guid FirmId,
    Guid? ChannelId,
    OpportunityStage Stage,
    DateTimeOffset OpenedAt,
    DateTimeOffset? QualifiedAt,
    DateTimeOffset? DecidedAt,
    IReadOnlyList<Guid> CompetitorIds,
    Guid? WinningProductId,
    Guid? CrossSellModuleProductId,
    bool IsCrossSell,
    bool IsDecided,
    string? Notes,
    IReadOnlyList<OpportunityScopeItemDto> Scope);

/// <summary>
/// Opens an opportunity: a firm seeking coverage for a scoped set of business processes.
/// A cross-sell is not a special case — it is an opportunity at an existing client scoped to new
/// processes, and <c>crossSellModuleProductId</c> names the module being sold.
/// </summary>
public sealed record CreateOpportunityRequest(
    [Required] Guid FirmId,
    DateTimeOffset? OpenedAt = null,
    Guid? ChannelId = null,
    Guid? CrossSellModuleProductId = null,
    IReadOnlyList<Guid>? ScopeProcessIds = null,
    IReadOnlyList<Guid>? CompetitorIds = null,
    string? Notes = null);

public sealed record UpdateOpportunityRequest(
    Guid? ChannelId = null,
    Guid? CrossSellModuleProductId = null,
    IReadOnlyList<Guid>? CompetitorIds = null,
    string? Notes = null);

/// <summary>Adds one business process to the scope. Scope can expand mid-cycle until the decision.</summary>
public sealed record AddOpportunityScopeRequest([Required] Guid BusinessProcessId, DateTimeOffset? AddedAt = null);

public sealed record QualifyOpportunityRequest(DateTimeOffset? At = null);

/// <summary>
/// Records the decision. <c>outcome</c> must be Won, Lost or NoDecision, and a won deal must name
/// the winning product (ours or a competitor's). Deciding also appends an
/// <c>OpportunityDecided</c> fact carrying a self-contained copy of the deal, which is what
/// calibration later reads — the ledger, not this row, is the history.
///
/// <para>
/// <c>at</c> is when the decision happened (the fact's effective date); <c>recordedAt</c> is when we
/// learned of it, and exists for the same import/backfill reason as on <c>POST /api/facts</c>:
/// loading last year's closed deals must not stamp them all as learned today, or every backdated
/// calibration reads an empty ledger. Omitted, it is stamped now; it may be backdated, never
/// post-dated.
/// </para>
/// </summary>
public sealed record DecideOpportunityRequest(
    OpportunityStage Outcome,
    Guid? WinningProductId = null,
    DateTimeOffset? At = null,
    DateTimeOffset? RecordedAt = null);

// ---------- Segment approach mix ----------

/// <summary>Share of a segment's firms using one alternative approach to a process.</summary>
public sealed record SegmentApproachAdoptionDto(Guid ApproachId, double Share);

public sealed record SetSegmentApproachAdoptionRequest([Required] Guid ApproachId, [Range(0, 1)] double Share);

// ---------- Calibration ----------

public sealed record CalibrationRunDto(
    Guid Id,
    Guid WorkspaceId,
    string? Name,
    DateOnly FactsAsOfDate,
    Guid PriorAssumptionSetId,
    Guid? FittedAssumptionSetId,
    bool IsExecuted,
    DateTimeOffset CreatedAt,
    string? Notes);

/// <summary>
/// Fits the prior assumption set to the facts ledger as it stood on <c>asOfDate</c>: only facts
/// recorded on or before that date are read, so a fit can honestly reconstruct what was known then.
/// The run is immutable and produces a new assumption set (parent = the prior, provenance
/// Calibrated) rather than editing the prior in place. <c>name</c> labels both the run and the
/// assumption set it produces; omitted, the set is named "{prior} (calibrated {asOfDate})".
/// </summary>
public sealed record CreateCalibrationRunRequest(
    [Required] Guid AssumptionSetId,
    DateOnly? AsOfDate = null,
    [MaxLength(200)] string? Name = null,
    string? Notes = null);

/// <summary>Prior vs posterior for one parameter, with the evidence that moved it.</summary>
public sealed record CalibrationParameterDiffDto(
    string Key,
    Guid? ClientSegmentId,
    SimulationFlowKind? FlowKind,
    Guid? ModuleProductId,
    Guid? ProductId,
    DistributionSpecDto? Prior,
    DistributionSpecDto? Posterior,
    double PriorMean,
    double PosteriorMean,
    double PriorStdDev,
    double PosteriorStdDev,
    double Shift,
    int SampleSize,
    string Rationale);

/// <summary>A prior the evidence could not move, passed through with the reason why.</summary>
public sealed record CalibrationUnfittedDto(
    string Key,
    Guid? ClientSegmentId,
    SimulationFlowKind? FlowKind,
    Guid? ModuleProductId,
    Guid? ProductId,
    DistributionSpecDto? Distribution,
    string Reason);

public sealed record CalibrationDiffDto(
    Guid CalibrationRunId,
    DateOnly AsOfDate,
    Guid PriorAssumptionSetId,
    Guid? FittedAssumptionSetId,
    IReadOnlyList<CalibrationParameterDiffDto> Fitted,
    IReadOnlyList<CalibrationUnfittedDto> Unfitted);

/// <summary>A calibration run plus the diff it produced — what POST returns.</summary>
public sealed record CalibrationRunResultDto(CalibrationRunDto Run, CalibrationDiffDto Diff);

public static class Phase3Mapping
{
    public static ChannelDto ToDto(this Channel c) =>
        new(c.Id, c.MarketId, c.Name, c.Description, c.OriginatesOpportunities, c.CarriesOnlyListedProducts,
            c.SegmentReaches
                .OrderBy(r => r.ClientSegmentId)
                .Select(r => new ChannelSegmentReachDto(r.ClientSegmentId, r.Reach, r.OriginationShare))
                .ToList(),
            c.ProductAffinities
                .OrderBy(a => a.ProductId)
                .Select(a => new ChannelProductAffinityDto(a.ProductId, a.Bias))
                .ToList());

    public static IntegrationDto ToDto(this Integration i) =>
        new(i.Id, i.ProductAId, i.ProductBId, i.QualityScore, i.Notes);

    public static FirmProductRelationshipDto ToDto(this FirmProductRelationship r) =>
        new(r.Id, r.FirmId, r.ProductId, r.ServedProcessIds, r.PriceLevel, MoneyDto.From(r.PricePaidPerPeriod),
            r.AcquisitionDate, r.EndDate, r.IsActive, r.Notes);

    public static OpportunityDto ToDto(this Opportunity o) =>
        new(o.Id, o.FirmId, o.ChannelId, o.Stage, o.OpenedAt, o.QualifiedAt, o.DecidedAt,
            o.CompetitorIds, o.WinningProductId, o.CrossSellModuleProductId, o.IsCrossSell, o.IsDecided, o.Notes,
            o.Scope
                .OrderBy(s => s.AddedAt).ThenBy(s => s.BusinessProcessId)
                .Select(s => new OpportunityScopeItemDto(s.BusinessProcessId, s.AddedAt))
                .ToList());

    public static CalibrationRunDto ToDto(this CalibrationRun r) =>
        new(r.Id, r.WorkspaceId, r.Name, r.FactsAsOfDate, r.PriorAssumptionSetId, r.FittedAssumptionSetId,
            r.IsExecuted, r.CreatedAt, r.Notes);
}
