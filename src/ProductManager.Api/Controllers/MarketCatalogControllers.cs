using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Competitive arenas. Phase 1 exposes create/read only — the full market UX arrives in Phase 2 —
/// but the API is the only write path, so the CLI seeds market data through these endpoints.
/// </summary>
[Route("api/markets")]
public sealed class MarketsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<MarketDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<MarketDto>> List([FromQuery] Guid? workspaceId, [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Markets.AsNoTracking()
            .Where(m => workspaceId == null || m.WorkspaceId == workspaceId)
            .OrderBy(m => m.Name)
            .ToPagedResponseAsync(paging, MarketMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MarketDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketDto>> Get(Guid id, CancellationToken ct)
    {
        var market = await db.Markets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return market is null ? NotFoundProblem("Market", id) : Ok(market.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(MarketDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<MarketDto>> Create([FromBody] CreateMarketRequest request, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return NotFoundProblem("Workspace", request.WorkspaceId);

        var market = new Market(request.WorkspaceId, request.Name, request.Description);
        db.Markets.Add(market);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = market.Id }, market.ToDto());
    }
}

/// <summary>Groups of firms with a shared profile.</summary>
[Route("api/client-segments")]
public sealed class ClientSegmentsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ClientSegmentDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<ClientSegmentDto>> List([FromQuery] Guid? marketId, [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.ClientSegments.AsNoTracking()
            .Where(s => marketId == null || s.MarketId == marketId)
            .OrderBy(s => s.Name)
            .ToPagedResponseAsync(paging, MarketMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ClientSegmentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClientSegmentDto>> Get(Guid id, CancellationToken ct)
    {
        var segment = await db.ClientSegments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return segment is null ? NotFoundProblem("ClientSegment", id) : Ok(segment.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ClientSegmentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ClientSegmentDto>> Create([FromBody] CreateClientSegmentRequest request,
        CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);

        var segment = new ClientSegment(request.MarketId, request.Name, request.Description);
        segment.SetTendencies(request.SuiteAffinity, request.ConsolidationPreference);
        db.ClientSegments.Add(segment);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = segment.Id }, segment.ToDto());
    }
}

/// <summary>The catalog of what firms must accomplish — the vocabulary demand and supply both point at.</summary>
[Route("api/business-processes")]
public sealed class BusinessProcessesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<BusinessProcessDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<BusinessProcessDto>> List(
        [FromQuery] Guid? marketId,
        [FromQuery] Guid? parentProcessId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.BusinessProcesses.AsNoTracking()
            .Where(p => (marketId == null || p.MarketId == marketId)
                        && (parentProcessId == null || p.ParentProcessId == parentProcessId))
            .OrderBy(p => p.Name)
            .ToPagedResponseAsync(paging, MarketMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BusinessProcessDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessProcessDto>> Get(Guid id, CancellationToken ct)
    {
        var process = await db.BusinessProcesses.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return process is null ? NotFoundProblem("BusinessProcess", id) : Ok(process.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(BusinessProcessDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<BusinessProcessDto>> Create([FromBody] CreateBusinessProcessRequest request,
        CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);
        if (request.ParentProcessId is not null &&
            !await db.BusinessProcesses.AnyAsync(p => p.Id == request.ParentProcessId, ct))
            return NotFoundProblem("BusinessProcess", request.ParentProcessId.Value);

        var process = new BusinessProcess(request.MarketId, request.Name, request.ParentProcessId)
        {
            Description = request.Description
        };
        db.BusinessProcesses.Add(process);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = process.Id }, process.ToDto());
    }
}

/// <summary>Alternative methods for a business process (the nullable approach dimension).</summary>
[Route("api/approaches")]
public sealed class ApproachesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ApproachDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<ApproachDto>> List([FromQuery] Guid? businessProcessId, [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Approaches.AsNoTracking()
            .Where(a => businessProcessId == null || a.BusinessProcessId == businessProcessId)
            .OrderBy(a => a.Name)
            .ToPagedResponseAsync(paging, MarketMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApproachDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApproachDto>> Get(Guid id, CancellationToken ct)
    {
        var approach = await db.Approaches.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return approach is null ? NotFoundProblem("Approach", id) : Ok(approach.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApproachDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApproachDto>> Create([FromBody] CreateApproachRequest request, CancellationToken ct)
    {
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == request.BusinessProcessId, ct))
            return NotFoundProblem("BusinessProcess", request.BusinessProcessId);

        var approach = new Approach(request.BusinessProcessId, request.Name) { Description = request.Description };
        db.Approaches.Add(approach);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = approach.Id }, approach.ToDto());
    }
}

/// <summary>Organizations owning rival products.</summary>
[Route("api/competitors")]
public sealed class CompetitorsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<CompetitorDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<CompetitorDto>> List([FromQuery] Guid? marketId, [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Competitors.AsNoTracking()
            .Where(c => marketId == null || c.MarketId == marketId)
            .OrderBy(c => c.Name)
            .ToPagedResponseAsync(paging, MarketMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CompetitorDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CompetitorDto>> Get(Guid id, CancellationToken ct)
    {
        var competitor = await db.Competitors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return competitor is null ? NotFoundProblem("Competitor", id) : Ok(competitor.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(CompetitorDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CompetitorDto>> Create([FromBody] CreateCompetitorRequest request,
        CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);

        var competitor = new Competitor(request.MarketId, request.Name) { StrategyNotes = request.StrategyNotes };
        db.Competitors.Add(competitor);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = competitor.Id }, competitor.ToDto());
    }
}

/// <summary>Sellable offerings, ours or a competitor's. A module is a product with a parent product.</summary>
[Route("api/products")]
public sealed class ProductsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProductDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<ProductDto>> List(
        [FromQuery] Guid? marketId,
        [FromQuery] Guid? competitorId,
        [FromQuery] Guid? parentProductId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Products.AsNoTracking()
            .Where(p => (marketId == null || p.MarketId == marketId)
                        && (competitorId == null || p.CompetitorId == competitorId)
                        && (parentProductId == null || p.ParentProductId == parentProductId))
            .OrderBy(p => p.Name)
            .ToPagedResponseAsync(paging, MarketMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFoundProblem("Product", id) : Ok(product.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);
        if (request.CompetitorId is not null && !await db.Competitors.AnyAsync(c => c.Id == request.CompetitorId, ct))
            return NotFoundProblem("Competitor", request.CompetitorId.Value);
        if (request.ParentProductId is not null && !await db.Products.AnyAsync(p => p.Id == request.ParentProductId, ct))
            return NotFoundProblem("Product", request.ParentProductId.Value);

        var product = new Product(request.MarketId, request.Name, request.CompetitorId, request.ParentProductId)
        {
            Description = request.Description
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product.ToDto());
    }

    /// <summary>Upserts pricing for the product; a null segment sets the default price.</summary>
    [HttpPut("{id:guid}/prices")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductDto>> SetPrice(Guid id, [FromBody] SetProductPriceRequest request,
        CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFoundProblem("Product", id);

        product.SetPrice(request.RecurringPricePerPeriod.ToDomain(), request.ImplementationFee.ToDomain(),
            request.ClientSegmentId, request.UsageBasedPlaceholder);
        await db.SaveChangesAsync(ct);
        return Ok(product.ToDto());
    }
}

/// <summary>
/// Supply assessments: (Product × BusinessProcess × Approach? × Segment?) → tier.
/// Unknown is recorded explicitly — it is not the same as bronze.
/// </summary>
[Route("api/coverages")]
public sealed class CoveragesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<CoverageDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<CoverageDto>> List(
        [FromQuery] Guid? productId,
        [FromQuery] Guid? businessProcessId,
        [FromQuery] Guid? clientSegmentId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Coverages.AsNoTracking()
            .Where(c => (productId == null || c.ProductId == productId)
                        && (businessProcessId == null || c.BusinessProcessId == businessProcessId)
                        && (clientSegmentId == null || c.ClientSegmentId == clientSegmentId))
            .OrderBy(c => c.ProductId).ThenBy(c => c.BusinessProcessId)
            .ToPagedResponseAsync(paging, MarketMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CoverageDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CoverageDto>> Get(Guid id, CancellationToken ct)
    {
        var coverage = await db.Coverages.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return coverage is null ? NotFoundProblem("Coverage", id) : Ok(coverage.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(CoverageDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CoverageDto>> Create([FromBody] CreateCoverageRequest request, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
            return NotFoundProblem("Product", request.ProductId);
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == request.BusinessProcessId, ct))
            return NotFoundProblem("BusinessProcess", request.BusinessProcessId);

        var coverage = request.IsUnknown
            ? Coverage.Unknown(request.ProductId, request.BusinessProcessId, request.ApproachId, request.ClientSegmentId)
            : Coverage.Assessed(request.ProductId, request.BusinessProcessId, request.Tier, request.Confidence,
                request.ApproachId, request.ClientSegmentId);

        coverage.EffectiveDate = request.EffectiveDate;
        coverage.Rationale = request.Rationale;

        db.Coverages.Add(coverage);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = coverage.Id }, coverage.ToDto());
    }
}
