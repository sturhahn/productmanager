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
///
/// <para>
/// <c>sources</c> makes the create repeat-safe: when any listed source record is already bound, the
/// bound relationship is returned (200) and nothing is created; every listed record is bound to
/// the relationship. Records bound to different relationships, or to another firm/product, are a
/// 409, and so is an unbound request when the firm already has an edge for this product — bind
/// records onto an existing edge through <c>POST /api/firms/bulk</c>, which checks the episode.
/// Without <c>sources</c> every call creates, as before.
/// </para>
/// </summary>
public sealed record CreateFirmProductRelationshipRequest(
    [Required] Guid FirmId,
    [Required] Guid ProductId,
    IReadOnlyList<Guid>? ServedProcessIds = null,
    [Range(0.0001, 1000)] double PriceLevel = 1.0,
    MoneyDto? PricePaidPerPeriod = null,
    DateOnly? AcquisitionDate = null,
    string? Notes = null,
    DateOnly? EndDate = null,
    IReadOnlyList<SourceRef>? Sources = null);

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

/// <summary>
/// One firm in a bulk import, with the relationships it already holds.
///
/// <para>
/// <b>Without <c>source</c></b> (legacy) the item is matched by (market, name) and overwrites the
/// firm: an absent segment, attributes or notes is written as null, an absent
/// <c>epistemicStatus</c> as Observed.
/// </para>
///
/// <para>
/// <b>With <c>source</c></b> the item is matched by its source binding first. A bound record updates
/// its firm, so a changed <c>name</c> is a rename (an exception when another firm in the market has
/// that name). An unbound record adopts the same-named firm and binds it, unless that firm is
/// already bound to a different record of the same source system, instance and object kind — then
/// the item is an exception ("indistinguishable same-name firms"): send a distinct, reviewed display
/// name instead. Otherwise a new firm is created and bound.
/// </para>
///
/// <para>
/// Source-bound items are <b>partial updates</b>. Source-owned fields: <c>name</c>,
/// <c>clientSegmentId</c>, <c>epistemicStatus</c>, <c>attributesJson</c>, <c>notes</c>. An absent (null)
/// field keeps the stored value; <c>clearFields</c> lists the fields to set to null (allowed:
/// <c>clientSegmentId</c>, <c>attributesJson</c>, <c>notes</c>). A field whose stored value was edited
/// locally since the last import (it differs from what this source last imported — for a field the
/// source never sent, from the value stored when the binding first saw it) is not overwritten: the
/// local value is kept, silently while the source still sends its last-imported value, and reported
/// under <c>conflicts</c> once the source's value differs from it. A conflict does not advance the
/// last-imported snapshot, so it is reported on every import until someone makes the local value
/// equal the source's.
/// </para>
/// </summary>
public sealed record FirmImportItem(
    [Required, MaxLength(200)] string Name,
    Guid? ClientSegmentId = null,
    EpistemicStatus? EpistemicStatus = null,
    string? AttributesJson = null,
    string? Notes = null,
    IReadOnlyList<FirmImportRelationship>? Relationships = null,
    SourceRef? Source = null,
    IReadOnlyList<string>? ClearFields = null);

/// <summary>
/// One firm/product edge in a bulk import. Omitting an edge from a later import never ends or
/// deletes it; <c>endDate</c> ends it.
///
/// <para>
/// <b>Without <c>sources</c></b> (legacy) the edge is matched by (firm, product) and overwritten; an
/// absent <c>priceLevel</c> is 1.0, an absent <c>endDate</c> leaves the stored one alone.
/// </para>
///
/// <para>
/// <b>With <c>sources</c></b> every listed source record is bound to the one edge for (firm, product)
/// — listing several is how records are deliberately consolidated. The model keeps one episode per
/// edge, so these are per-item exceptions that leave the stored edge untouched: the same product
/// twice for one firm in a request; a listed record already bound to another edge; an edge already
/// bound to other source records none of which is listed (consolidation must be declared); a new
/// record whose dates describe a different episode (starts after the stored end, or ends before the
/// stored start). Source-owned fields: <c>acquisitionDate</c>, <c>endDate</c>, <c>pricePaidPerPeriod</c>,
/// <c>priceLevel</c>, <c>servedProcessIds</c> — absent keeps, <c>clearFields</c> clears
/// (<c>acquisitionDate</c>, <c>endDate</c>, <c>pricePaidPerPeriod</c>, <c>servedProcessIds</c>), and
/// local edits are reported as conflicts exactly as for firms.
/// </para>
/// </summary>
public sealed record FirmImportRelationship(
    [Required] Guid ProductId,
    IReadOnlyList<Guid>? ServedProcessIds = null,
    [Range(0.0001, 1000)] double? PriceLevel = null,
    MoneyDto? PricePaidPerPeriod = null,
    DateOnly? AcquisitionDate = null,
    DateOnly? EndDate = null,
    IReadOnlyList<SourceRef>? Sources = null,
    IReadOnlyList<string>? ClearFields = null);

/// <summary>
/// Imports a roster of firms into one market. Legacy items match by natural key (market, name), so
/// re-importing the same roster updates rather than duplicates it; source-bound items match by
/// source identity (see <see cref="FirmImportItem"/>). An unknown segment or product fails the whole
/// request (404) before anything is written; identity and episode problems are per-item exceptions
/// that do not stop the other items.
/// </summary>
public sealed record BulkImportFirmsRequest(
    [Required] Guid MarketId,
    [Required] IReadOnlyList<FirmImportItem> Firms);

/// <summary>
/// Counters, the firms written, and one outcome per request item (same order). <c>firms</c> omits
/// exception items. <c>relationshipsWritten</c> counts every legacy edge and each created or updated
/// source-bound edge.
/// </summary>
public sealed record BulkImportFirmsResponse(
    Guid MarketId,
    int Created,
    int Updated,
    int RelationshipsWritten,
    IReadOnlyList<FirmDto> Firms,
    int Unchanged,
    int Exceptions,
    IReadOnlyList<FirmImportOutcomeDto> Outcomes);

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
///
/// <para>
/// With <c>source</c>, the create is repeat-safe: an already-bound source record returns its
/// opportunity (200) and creates nothing — whatever else the request says, so a retry after a lost
/// response is harmless; advance it with qualify/decide. A bound record whose opportunity belongs to
/// a different firm is a 409 <see cref="ProblemTypes.CorrectionRequired"/>. Without <c>source</c>
/// every call creates, as before.
/// </para>
/// </summary>
public sealed record CreateOpportunityRequest(
    [Required] Guid FirmId,
    DateTimeOffset? OpenedAt = null,
    Guid? ChannelId = null,
    Guid? CrossSellModuleProductId = null,
    IReadOnlyList<Guid>? ScopeProcessIds = null,
    IReadOnlyList<Guid>? CompetitorIds = null,
    string? Notes = null,
    SourceRef? Source = null);

public sealed record UpdateOpportunityRequest(
    Guid? ChannelId = null,
    Guid? CrossSellModuleProductId = null,
    IReadOnlyList<Guid>? CompetitorIds = null,
    string? Notes = null);

/// <summary>Adds one business process to the scope. Scope can expand mid-cycle until the decision.</summary>
public sealed record AddOpportunityScopeRequest([Required] Guid BusinessProcessId, DateTimeOffset? AddedAt = null);

/// <summary>
/// Qualifies an open opportunity. Repeat-safe: qualifying again at the same instant (or with
/// <c>at</c> omitted, once qualified) returns 200 and changes nothing; a different date on an
/// already-qualified opportunity is a 409 <see cref="ProblemTypes.CorrectionRequired"/>.
/// </summary>
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
///
/// <para>
/// Repeat-safe for every opportunity: repeating the recorded decision (same outcome, winning
/// product and <c>at</c> — an omitted <c>at</c> matches the recorded one) returns 200 and appends no
/// second fact. A different outcome, winner or date on a decided opportunity is a 409 whose
/// ProblemDetails <c>type</c> is <see cref="ProblemTypes.CorrectionRequired"/>; the opportunity and
/// its fact are unchanged — history is not rewritten.
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
    string? Notes,
    DateOnly? ObservationsRecordedAfter = null);

/// <summary>
/// Fits the prior assumption set to the facts ledger as it stood on <c>asOfDate</c>: only facts
/// recorded on or before that date are read, so a fit can honestly reconstruct what was known then.
/// The run is immutable and produces a new assumption set (parent = the prior, provenance
/// Calibrated) rather than editing the prior in place. <c>name</c> labels both the run and the
/// assumption set it produces; omitted, the set is named "{prior} (calibrated {asOfDate})".
///
/// <para>
/// <c>observationsRecordedAfter</c> narrows the fit to facts recorded <em>after</em> that date (and
/// starts relationship exposure there), so a prior informed by earlier evidence can be updated with
/// new evidence only. It must be before <c>asOfDate</c>.
/// </para>
///
/// <para>
/// Evidence reuse is refused before fitting (409, <see cref="ProblemTypes.PriorEvidenceReuse"/>, nothing
/// stored). For each prior parameter the engine would move, the prior's evidence is compared with the
/// observations this fit would apply to it, by every rule whose field is present: <c>Independent</c> never
/// overlaps; <c>recordedThrough</c> R overlaps unless <c>observationsRecordedAfter</c> &gt;= R; an observation
/// period [from, through] overlaps if any of the fit's observations for the parameter is effective inside it
/// (for ChurnHazard and CrossSellRate: also any client-month inside it; exposure runs from day
/// <c>observationsRecordedAfter</c>, so only a window after <c>through</c> leaves the period's client-months
/// out). Unknown evidence is not refused; the readiness report flags it. A fit stamps every parameter it moves
/// with <c>recordedThrough</c> = the as-of date and its run id, keeping the updated prior's own period, so
/// both rules protect a fitted set fitted again.
/// </para>
/// </summary>
public sealed record CreateCalibrationRunRequest(
    [Required] Guid AssumptionSetId,
    DateOnly? AsOfDate = null,
    [MaxLength(200)] string? Name = null,
    string? Notes = null,
    DateOnly? ObservationsRecordedAfter = null);

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
            r.IsExecuted, r.CreatedAt, r.Notes, r.ObservationsRecordedAfter);
}
