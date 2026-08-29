using System.ComponentModel.DataAnnotations;
using ProductManager.Domain.Common;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Contracts;

// ---------- Market ----------

public sealed record MarketDto(Guid Id, Guid WorkspaceId, string Name, string? Description);

public sealed record CreateMarketRequest(
    [Required] Guid WorkspaceId,
    [Required, MaxLength(200)] string Name,
    string? Description);

// ---------- ClientSegment ----------

public sealed record ClientSegmentDto(
    Guid Id,
    Guid MarketId,
    string Name,
    string? Description,
    double SuiteAffinity,
    double ConsolidationPreference);

public sealed record CreateClientSegmentRequest(
    [Required] Guid MarketId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    [Range(0, 1)] double SuiteAffinity = 0.5,
    [Range(0, 1)] double ConsolidationPreference = 0.5);

// ---------- BusinessProcess / Approach ----------

public sealed record BusinessProcessDto(Guid Id, Guid MarketId, Guid? ParentProcessId, string Name, string? Description);

public sealed record CreateBusinessProcessRequest(
    [Required] Guid MarketId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? ParentProcessId);

public sealed record ApproachDto(Guid Id, Guid BusinessProcessId, string Name, string? Description);

public sealed record CreateApproachRequest(
    [Required] Guid BusinessProcessId,
    [Required, MaxLength(200)] string Name,
    string? Description);

// ---------- Product ----------

public sealed record MoneyDto([Range(0, double.MaxValue)] decimal Amount, [Required, MinLength(3), MaxLength(3)] string Currency)
{
    public Money ToDomain() => new(Amount, Currency);

    public static MoneyDto? From(Money? money) => money is null ? null : new MoneyDto(money.Amount, money.Currency);
}

public sealed record ProductPriceDto(
    Guid? ClientSegmentId,
    MoneyDto RecurringPricePerPeriod,
    MoneyDto ImplementationFee,
    string? UsageBasedPlaceholder);

public sealed record ProductDto(
    Guid Id,
    Guid MarketId,
    Guid? CompetitorId,
    Guid? ParentProductId,
    string Name,
    string? Description,
    bool IsOurs,
    bool IsModule,
    IReadOnlyList<ProductPriceDto> Prices);

public sealed record CreateProductRequest(
    [Required] Guid MarketId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? CompetitorId,
    Guid? ParentProductId);

public sealed record SetProductPriceRequest(
    [Required] MoneyDto RecurringPricePerPeriod,
    [Required] MoneyDto ImplementationFee,
    Guid? ClientSegmentId,
    string? UsageBasedPlaceholder);

// ---------- Competitor ----------

public sealed record CompetitorDto(Guid Id, Guid MarketId, string Name, string? StrategyNotes);

public sealed record CreateCompetitorRequest(
    [Required] Guid MarketId,
    [Required, MaxLength(200)] string Name,
    string? StrategyNotes);

// ---------- Coverage ----------

public sealed record CoverageDto(
    Guid Id,
    Guid ProductId,
    Guid BusinessProcessId,
    Guid? ApproachId,
    Guid? ClientSegmentId,
    CoverageTier Tier,
    bool IsUnknown,
    double Confidence,
    double? NumericScore,
    DateOnly? EffectiveDate,
    string? Rationale,
    string? ScaleLimits);

/// <summary>
/// Records a coverage assessment. <c>isUnknown = true</c> records an explicit "we don't know"
/// (distinct from NotPlaying and from Bronze) and ignores tier/confidence.
/// </summary>
public sealed record CreateCoverageRequest(
    [Required] Guid ProductId,
    [Required] Guid BusinessProcessId,
    CoverageTier Tier,
    bool IsUnknown = false,
    [Range(0, 1)] double Confidence = 1.0,
    Guid? ApproachId = null,
    Guid? ClientSegmentId = null,
    DateOnly? EffectiveDate = null,
    string? Rationale = null);

public static class MarketMapping
{
    public static MarketDto ToDto(this Market m) => new(m.Id, m.WorkspaceId, m.Name, m.Description);

    public static ClientSegmentDto ToDto(this ClientSegment s) =>
        new(s.Id, s.MarketId, s.Name, s.Description, s.SuiteAffinity, s.ConsolidationPreference);

    public static BusinessProcessDto ToDto(this BusinessProcess p) =>
        new(p.Id, p.MarketId, p.ParentProcessId, p.Name, p.Description);

    public static ApproachDto ToDto(this Approach a) => new(a.Id, a.BusinessProcessId, a.Name, a.Description);

    public static CompetitorDto ToDto(this Competitor c) => new(c.Id, c.MarketId, c.Name, c.StrategyNotes);

    public static ProductDto ToDto(this Product p) => new(
        p.Id, p.MarketId, p.CompetitorId, p.ParentProductId, p.Name, p.Description, p.IsOurs, p.IsModule,
        p.Prices.Select(price => new ProductPriceDto(
            price.ClientSegmentId,
            MoneyDto.From(price.RecurringPricePerPeriod)!,
            MoneyDto.From(price.ImplementationFee)!,
            price.UsageBasedPlaceholder)).ToList());

    public static CoverageDto ToDto(this Coverage c) => new(
        c.Id, c.ProductId, c.BusinessProcessId, c.ApproachId, c.ClientSegmentId, c.Tier, c.IsUnknown,
        c.Confidence, c.NumericScore, c.EffectiveDate, c.Rationale, c.ScaleLimits);
}
