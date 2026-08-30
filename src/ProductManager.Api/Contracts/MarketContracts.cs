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

public sealed record UpdateMarketRequest(
    [Required, MaxLength(200)] string Name,
    string? Description);

// ---------- ClientSegment ----------

/// <summary>
/// The segment's observed starting state — the stocks and unit economics the simulation engine
/// starts a run from. Amounts are per month in the workspace's implicit single currency.
/// </summary>
public sealed record SegmentStockStateDto(
    double InitialProspectPool,
    int InitialClientCount,
    double ArpuPerMonth,
    double ImplementationFee,
    double ServicingCostPerClient,
    double InfraFixedCostPerMonth,
    double InfraCostPerClient,
    double RdDragPerMonth);

/// <summary>Starting penetration and pricing of one cross-sellable module within the segment.</summary>
public sealed record SegmentModulePenetrationDto(Guid ModuleProductId, double Penetration, double ArpuPerMonth);

public sealed record ClientSegmentDto(
    Guid Id,
    Guid MarketId,
    string Name,
    string? Description,
    double SuiteAffinity,
    double ConsolidationPreference,
    SegmentStockStateDto StockState,
    IReadOnlyList<SegmentModulePenetrationDto> ModulePenetrations,
    IReadOnlyList<SegmentApproachAdoptionDto> ApproachAdoptions);

public sealed record CreateClientSegmentRequest(
    [Required] Guid MarketId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    [Range(0, 1)] double SuiteAffinity = 0.5,
    [Range(0, 1)] double ConsolidationPreference = 0.5);

public sealed record UpdateClientSegmentRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    [Range(0, 1)] double SuiteAffinity = 0.5,
    [Range(0, 1)] double ConsolidationPreference = 0.5);

/// <summary>Replaces the segment's whole starting state — it is one measurement, not a set of knobs.</summary>
public sealed record SetSegmentStockStateRequest(
    [Range(0, double.MaxValue)] double InitialProspectPool = 0,
    [Range(0, int.MaxValue)] int InitialClientCount = 0,
    [Range(0, double.MaxValue)] double ArpuPerMonth = 0,
    [Range(0, double.MaxValue)] double ImplementationFee = 0,
    [Range(0, double.MaxValue)] double ServicingCostPerClient = 0,
    [Range(0, double.MaxValue)] double InfraFixedCostPerMonth = 0,
    [Range(0, double.MaxValue)] double InfraCostPerClient = 0,
    [Range(0, double.MaxValue)] double RdDragPerMonth = 0);

public sealed record SetSegmentModulePenetrationRequest(
    [Required] Guid ModuleProductId,
    [Range(0, 1)] double Penetration,
    [Range(0, double.MaxValue)] double ArpuPerMonth);

// ---------- BusinessProcess / Approach ----------

public sealed record BusinessProcessDto(Guid Id, Guid MarketId, Guid? ParentProcessId, string Name, string? Description);

public sealed record CreateBusinessProcessRequest(
    [Required] Guid MarketId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? ParentProcessId);

public sealed record UpdateBusinessProcessRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? ParentProcessId);

public sealed record ApproachDto(Guid Id, Guid BusinessProcessId, string Name, string? Description);

public sealed record CreateApproachRequest(
    [Required] Guid BusinessProcessId,
    [Required, MaxLength(200)] string Name,
    string? Description);

public sealed record UpdateApproachRequest(
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

public sealed record UpdateProductRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
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

public sealed record UpdateCompetitorRequest(
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
/// Records a coverage assessment, upserting on the (product, process, approach?, segment?) key —
/// that tuple is the identity of an assessment, so posting it twice re-assesses rather than
/// duplicating. <c>isUnknown = true</c> records an explicit "we don't know" (distinct from
/// NotPlaying and from Bronze) and ignores tier/confidence.
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

/// <summary>Re-assesses an existing coverage row found by id; the key dimensions are immutable.</summary>
public sealed record UpdateCoverageRequest(
    CoverageTier Tier,
    bool IsUnknown = false,
    [Range(0, 1)] double Confidence = 1.0,
    DateOnly? EffectiveDate = null,
    string? Rationale = null,
    string? ScaleLimits = null);

public static class MarketMapping
{
    public static MarketDto ToDto(this Market m) => new(m.Id, m.WorkspaceId, m.Name, m.Description);

    public static ClientSegmentDto ToDto(this ClientSegment s) =>
        new(s.Id, s.MarketId, s.Name, s.Description, s.SuiteAffinity, s.ConsolidationPreference,
            new SegmentStockStateDto(
                s.StockState.InitialProspectPool,
                s.StockState.InitialClientCount,
                s.StockState.ArpuPerMonth,
                s.StockState.ImplementationFee,
                s.StockState.ServicingCostPerClient,
                s.StockState.InfraFixedCostPerMonth,
                s.StockState.InfraCostPerClient,
                s.StockState.RdDragPerMonth),
            s.ModulePenetrations
                .OrderBy(m => m.ModuleProductId)
                .Select(m => new SegmentModulePenetrationDto(m.ModuleProductId, m.Penetration, m.ArpuPerMonth))
                .ToList(),
            s.ApproachAdoptions
                .OrderBy(a => a.ApproachId)
                .Select(a => new SegmentApproachAdoptionDto(a.ApproachId, a.Share))
                .ToList());

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
