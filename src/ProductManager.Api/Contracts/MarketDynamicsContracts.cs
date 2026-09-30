using System.ComponentModel.DataAnnotations;
using ProductManager.Domain.Common;
using ProductManager.Domain.Facts;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Contracts;

// ---------- Firm ----------

public sealed record FirmDto(
    Guid Id,
    Guid MarketId,
    Guid? ClientSegmentId,
    string Name,
    EpistemicStatus EpistemicStatus,
    string? AttributesJson,
    string? Notes);

public sealed record CreateFirmRequest(
    [Required] Guid MarketId,
    [Required, MaxLength(200)] string Name,
    EpistemicStatus EpistemicStatus = EpistemicStatus.Observed,
    Guid? ClientSegmentId = null,
    string? AttributesJson = null,
    string? Notes = null);

public sealed record UpdateFirmRequest(
    [Required, MaxLength(200)] string Name,
    EpistemicStatus EpistemicStatus = EpistemicStatus.Observed,
    Guid? ClientSegmentId = null,
    string? AttributesJson = null,
    string? Notes = null);

// ---------- Need ----------

public sealed record NeedDto(
    Guid Id,
    Guid? FirmId,
    Guid? ClientSegmentId,
    Guid BusinessProcessId,
    double ImportanceWeight,
    bool IsHard,
    MoneyDto? WorkaroundCostPerPeriod,
    string? RequiredDepthJson,
    string? Conditionality,
    string? Notes);

/// <summary>
/// A demand edge. Exactly one of <c>firmId</c> / <c>clientSegmentId</c> must be given: a need is
/// either one firm's or a whole segment's.
/// </summary>
public sealed record CreateNeedRequest(
    [Required] Guid BusinessProcessId,
    [Range(0, double.MaxValue)] double ImportanceWeight,
    Guid? FirmId = null,
    Guid? ClientSegmentId = null,
    bool IsHard = false,
    MoneyDto? WorkaroundCostPerPeriod = null,
    string? RequiredDepthJson = null,
    string? Conditionality = null,
    string? Notes = null);

public sealed record UpdateNeedRequest(
    [Range(0, double.MaxValue)] double ImportanceWeight,
    bool IsHard = false,
    MoneyDto? WorkaroundCostPerPeriod = null,
    string? RequiredDepthJson = null,
    string? Conditionality = null,
    string? Notes = null);

// ---------- MarketEvent ----------

/// <summary>How an event scales one simulated flow while it is active. A null segment means all segments.</summary>
public sealed record EventRateMultiplierDto(
    Guid? ClientSegmentId,
    SimulationFlowKind FlowKind,
    [Range(0, double.MaxValue)] double Multiplier);

public sealed record MarketEventDto(
    Guid Id,
    Guid MarketId,
    MarketEventType Type,
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate,
    double? Magnitude,
    bool IsHypothetical,
    Guid? CompetitorId,
    string? Notes,
    IReadOnlyList<EventRateMultiplierDto> RateMultipliers);

public sealed record CreateMarketEventRequest(
    [Required] Guid MarketId,
    MarketEventType Type,
    [Required, MaxLength(200)] string Name,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    bool IsHypothetical = false,
    double? Magnitude = null,
    Guid? CompetitorId = null,
    string? Notes = null,
    IReadOnlyList<EventRateMultiplierDto>? RateMultipliers = null);

public sealed record UpdateMarketEventRequest(
    MarketEventType Type,
    [Required, MaxLength(200)] string Name,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    bool IsHypothetical = false,
    double? Magnitude = null,
    Guid? CompetitorId = null,
    string? Notes = null,
    IReadOnlyList<EventRateMultiplierDto>? RateMultipliers = null);

// ---------- Covariate ----------

public sealed record CovariateDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    string? Unit,
    double? GbmStart,
    double? GbmDrift,
    double? GbmVol,
    bool HasGbmSpec);

/// <summary>
/// A named external time series. The three <c>gbm*</c> numbers describe the *future* path
/// (geometric Brownian motion, per-month log drift and volatility) and must be given together or
/// not at all; observed history arrives separately as facts.
/// </summary>
public sealed record CreateCovariateRequest(
    [Required] Guid WorkspaceId,
    [Required, MaxLength(200)] string Name,
    string? Description = null,
    string? Unit = null,
    double? GbmStart = null,
    double? GbmDrift = null,
    double? GbmVol = null);

public sealed record UpdateCovariateRequest(
    [Required, MaxLength(200)] string Name,
    string? Description = null,
    string? Unit = null,
    double? GbmStart = null,
    double? GbmDrift = null,
    double? GbmVol = null);

/// <summary>One observed value of a covariate, read back out of the bi-temporal facts ledger.</summary>
public sealed record CovariateObservationDto(
    Guid FactEventId,
    DateOnly EffectiveDate,
    DateTimeOffset RecordedDate,
    double Value,
    string? Notes);

public sealed record CovariateHistoryDto(
    Guid CovariateId,
    string Name,
    string? Unit,
    IReadOnlyList<CovariateObservationDto> Observations);

public static class MarketDynamicsMapping
{
    public static FirmDto ToDto(this Firm f) =>
        new(f.Id, f.MarketId, f.ClientSegmentId, f.Name, f.EpistemicStatus, f.AttributesJson, f.Notes);

    public static NeedDto ToDto(this Need n) =>
        new(n.Id, n.FirmId, n.ClientSegmentId, n.BusinessProcessId, n.ImportanceWeight, n.IsHard,
            MoneyDto.From(n.WorkaroundCostPerPeriod), n.RequiredDepthJson, n.Conditionality, n.Notes);

    public static MarketEventDto ToDto(this MarketEvent e) =>
        new(e.Id, e.MarketId, e.Type, e.Name, e.StartDate, e.EndDate, e.Magnitude, e.IsHypothetical,
            e.CompetitorId, e.Notes,
            e.RateMultipliers
                .Select(m => new EventRateMultiplierDto(m.ClientSegmentId, m.FlowKind, m.Multiplier))
                .ToList());

    public static CovariateDto ToDto(this Covariate c) =>
        new(c.Id, c.WorkspaceId, c.Name, c.Description, c.Unit, c.GbmStart, c.GbmDrift, c.GbmVol, c.HasGbmSpec);

    public static IReadOnlyList<MarketEventRateMultiplier> ToDomain(
        this IReadOnlyList<EventRateMultiplierDto>? multipliers) =>
        multipliers is null
            ? []
            : multipliers
                .Select(m => new MarketEventRateMultiplier(m.ClientSegmentId, m.FlowKind, m.Multiplier))
                .ToList();
}

// ---------- Facts ledger ----------

/// <summary>
/// A bi-temporal market observation. <c>effectiveDate</c> is when it was true in the world;
/// the recorded date is stamped by the server, so "what we knew as of D" stays honest.
/// The ledger is append-only: there is no update and no delete.
/// </summary>
public sealed record FactEventDto(
    Guid Id,
    Guid WorkspaceId,
    FactEventKind Kind,
    DateOnly EffectiveDate,
    DateTimeOffset RecordedDate,
    string? SubjectType,
    Guid? SubjectId,
    string? PayloadJson,
    string? Notes);

/// <summary>
/// Appends one observation. For a covariate reading use
/// <c>kind = CovariateObserved</c>, <c>subjectType = "Covariate"</c>, <c>subjectId</c> = the
/// covariate, and a payload of <c>{"value": 1234.5}</c> — that is what the covariate history
/// endpoint reads back.
///
/// <para>
/// <c>recordedAt</c> is the <b>import / backfill path</b>: when history is loaded from a CRM export,
/// a seed file or a transcript, the honest recorded date is when that observation was originally
/// known, not when the loader ran. Omit it for a live observation and the server stamps now. It may
/// be backdated but never post-dated — a fact cannot have been known before it was written — and
/// <c>effectiveDate</c> rules are unchanged either way (a fact may be effective before, on, or after
/// the date it was recorded).
/// </para>
///
/// <para>
/// <c>source</c> gives the observation a source event identity, which is what tells two genuine
/// same-day events apart from one event posted twice. A bound source record whose fact is materially
/// identical (kind, effective date, subject, payload — notes and <c>recordedAt</c> are not material)
/// returns that fact (200) and appends nothing; materially different is a 409
/// <see cref="ProblemTypes.CorrectionRequired"/> and appends nothing. Without <c>source</c> every
/// call appends, as before.
/// </para>
/// </summary>
public sealed record CreateFactEventRequest(
    [Required] Guid WorkspaceId,
    FactEventKind Kind,
    DateOnly EffectiveDate,
    string? SubjectType = null,
    Guid? SubjectId = null,
    string? PayloadJson = null,
    string? Notes = null,
    DateTimeOffset? RecordedAt = null,
    SourceRef? Source = null);

public static class FactMapping
{
    /// <summary>Subject type recorded on covariate observations.</summary>
    public const string CovariateSubjectType = "Covariate";

    /// <summary>Payload property carrying the observed number.</summary>
    public const string ValuePayloadProperty = "value";

    public static FactEventDto ToDto(this FactEvent f) =>
        new(f.Id, f.WorkspaceId, f.Kind, f.EffectiveDate, f.RecordedDate, f.SubjectType, f.SubjectId,
            f.PayloadJson, f.Notes);
}
