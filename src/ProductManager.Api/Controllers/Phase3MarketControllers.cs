using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Imports;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Routes to market: reach into segments, opportunity origination, per-product bias. Direct sales
/// is just another channel — there is no special-case "direct" entity, and an opportunity with no
/// channel is simply unrouted.
/// </summary>
[Route("api/channels")]
public sealed class ChannelsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ChannelDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<ChannelDto>> List(
        [FromQuery] Guid? marketId,
        [FromQuery] bool? originatesOpportunities,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Channels.AsNoTracking()
            .Where(c => (marketId == null || c.MarketId == marketId)
                        && (originatesOpportunities == null || c.OriginatesOpportunities == originatesOpportunities))
            .OrderBy(c => c.Name)
            .ToPagedResponseAsync(paging, Phase3Mapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelDto>> Get(Guid id, CancellationToken ct)
    {
        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return channel is null ? NotFoundProblem("Channel", id) : Ok(channel.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ChannelDto>> Create([FromBody] CreateChannelRequest request, CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);

        var channel = new Channel(request.MarketId, request.Name)
        {
            Description = request.Description,
            OriginatesOpportunities = request.OriginatesOpportunities,
            CarriesOnlyListedProducts = request.CarriesOnlyListedProducts
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = channel.Id }, channel.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelDto>> Update(Guid id, [FromBody] UpdateChannelRequest request,
        CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return NotFoundProblem("Channel", id);

        channel.Rename(request.Name);
        channel.Description = request.Description;
        channel.OriginatesOpportunities = request.OriginatesOpportunities;
        channel.CarriesOnlyListedProducts = request.CarriesOnlyListedProducts;
        await db.SaveChangesAsync(ct);
        return Ok(channel.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return NotFoundProblem("Channel", id);

        db.Channels.Remove(channel);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sets (upserting by segment) how much of a segment this channel reaches and originates.</summary>
    [HttpPut("{id:guid}/segment-reaches")]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelDto>> SetSegmentReach(Guid id,
        [FromBody] SetChannelSegmentReachRequest request, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return NotFoundProblem("Channel", id);
        if (!await db.ClientSegments.AnyAsync(s => s.Id == request.ClientSegmentId, ct))
            return NotFoundProblem("ClientSegment", request.ClientSegmentId);

        channel.SetSegmentReach(request.ClientSegmentId, request.Reach, request.OriginationShare);
        await db.SaveChangesAsync(ct);
        return Ok(channel.ToDto());
    }

    [HttpDelete("{id:guid}/segment-reaches/{clientSegmentId:guid}")]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelDto>> RemoveSegmentReach(Guid id, Guid clientSegmentId, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return NotFoundProblem("Channel", id);
        if (!channel.RemoveSegmentReach(clientSegmentId))
            return NotFoundProblem("Channel reach into segment", clientSegmentId);

        await db.SaveChangesAsync(ct);
        return Ok(channel.ToDto());
    }

    /// <summary>Sets (upserting by product) this channel's signed bias toward a product.</summary>
    [HttpPut("{id:guid}/product-affinities")]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelDto>> SetProductAffinity(Guid id,
        [FromBody] SetChannelProductAffinityRequest request, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return NotFoundProblem("Channel", id);
        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
            return NotFoundProblem("Product", request.ProductId);

        channel.SetProductAffinity(request.ProductId, request.Bias);
        await db.SaveChangesAsync(ct);
        return Ok(channel.ToDto());
    }

    [HttpDelete("{id:guid}/product-affinities/{productId:guid}")]
    [ProducesResponseType(typeof(ChannelDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelDto>> RemoveProductAffinity(Guid id, Guid productId, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return NotFoundProblem("Channel", id);
        if (!channel.RemoveProductAffinity(productId))
            return NotFoundProblem("Channel affinity for product", productId);

        await db.SaveChangesAsync(ct);
        return Ok(channel.ToDto());
    }
}

/// <summary>
/// Product↔Product edges with a quality score, including integrations with partial competitors.
/// They are what makes a best-of-breed choice rational: a candidate is judged partly on how well it
/// fits the stack a firm already runs.
/// </summary>
[Route("api/integrations")]
public sealed class IntegrationsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<IntegrationDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<IntegrationDto>> List(
        [FromQuery] Guid? productId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Integrations.AsNoTracking()
            .Where(i => productId == null || i.ProductAId == productId || i.ProductBId == productId)
            .OrderBy(i => i.ProductAId).ThenBy(i => i.ProductBId)
            .ToPagedResponseAsync(paging, Phase3Mapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(IntegrationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<IntegrationDto>> Get(Guid id, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return integration is null ? NotFoundProblem("Integration", id) : Ok(integration.ToDto());
    }

    /// <summary>
    /// Creates the edge. The edge is symmetric, so (A, B) and (B, A) are the same integration and
    /// the second one is a conflict rather than a second row — two rows would let the same pair
    /// carry two different quality scores.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(IntegrationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IntegrationDto>> Create([FromBody] CreateIntegrationRequest request,
        CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == request.ProductAId, ct))
            return NotFoundProblem("Product", request.ProductAId);
        if (!await db.Products.AnyAsync(p => p.Id == request.ProductBId, ct))
            return NotFoundProblem("Product", request.ProductBId);

        bool exists = await db.Integrations.AnyAsync(
            i => (i.ProductAId == request.ProductAId && i.ProductBId == request.ProductBId)
                 || (i.ProductAId == request.ProductBId && i.ProductBId == request.ProductAId), ct);
        if (exists)
            return ConflictProblem("These two products are already integrated; update that edge instead.");

        var integration = new Integration(request.ProductAId, request.ProductBId, request.QualityScore)
        {
            Notes = request.Notes
        };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = integration.Id }, integration.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(IntegrationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<IntegrationDto>> Update(Guid id, [FromBody] UpdateIntegrationRequest request,
        CancellationToken ct)
    {
        var integration = await db.Integrations.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (integration is null) return NotFoundProblem("Integration", id);

        integration.SetQuality(request.QualityScore);
        integration.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(integration.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var integration = await db.Integrations.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (integration is null) return NotFoundProblem("Integration", id);

        db.Integrations.Remove(integration);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>
/// "Being a client" as an edge: firm holds product, serving these processes, at this share of list
/// price, from this date. The same shape carries competitors' client relationships — which is what
/// makes an incumbent's position measurable rather than assumed.
/// </summary>
[Route("api/firm-relationships")]
public sealed class FirmRelationshipsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<FirmProductRelationshipDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<FirmProductRelationshipDto>> List(
        [FromQuery] Guid? firmId,
        [FromQuery] Guid? productId,
        [FromQuery] bool? activeOnly,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.FirmProductRelationships.AsNoTracking()
            .Where(r => (firmId == null || r.FirmId == firmId)
                        && (productId == null || r.ProductId == productId)
                        && (activeOnly != true || r.EndDate == null))
            .OrderBy(r => r.FirmId).ThenBy(r => r.ProductId)
            .ToPagedResponseAsync(paging, Phase3Mapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FirmProductRelationshipDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FirmProductRelationshipDto>> Get(Guid id, CancellationToken ct)
    {
        var relationship = await db.FirmProductRelationships.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return relationship is null ? NotFoundProblem("FirmProductRelationship", id) : Ok(relationship.ToDto());
    }

    /// <summary>
    /// Creates an edge. With <c>sources</c>, an already-bound source record returns its edge (200)
    /// instead, binding any listed records not bound yet.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FirmProductRelationshipDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(FirmProductRelationshipDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FirmProductRelationshipDto>> Create(
        [FromBody] CreateFirmProductRelationshipRequest request, CancellationToken ct)
    {
        var firm = await db.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FirmId, ct);
        if (firm is null) return NotFoundProblem("Firm", request.FirmId);
        if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
            return NotFoundProblem("Product", request.ProductId);

        var sources = request.Sources?.Distinct().ToList() ?? [];
        Guid workspaceId = default;
        var bindings = new List<SourceBinding?>();
        FirmProductRelationship? bound = null;
        if (sources.Count > 0)
        {
            workspaceId = await db.Markets.Where(m => m.Id == firm.MarketId).Select(m => m.WorkspaceId).FirstAsync(ct);
            foreach (var source in sources) bindings.Add(await db.FindBindingAsync(workspaceId, source, ct));
            if (bindings.Any(b => b is not null && b.TargetType != nameof(FirmProductRelationship)))
                return ConflictProblem("A listed source record is bound to something other than a relationship.");
            var boundIds = bindings.OfType<SourceBinding>().Select(b => b.TargetId).Distinct().ToList();
            var boundEdges = await db.FirmProductRelationships.Where(r => boundIds.Contains(r.Id)).ToListAsync(ct);
            if (boundEdges.Count > 1)
                return ConflictProblem("The listed source records are bound to different relationships.");
            bound = boundEdges.SingleOrDefault();
            if (bound is not null && (bound.FirmId != request.FirmId || bound.ProductId != request.ProductId))
                return CorrectionRequiredProblem(
                    $"The listed source records are already relationship {bound.Id} of firm {bound.FirmId} " +
                    $"and product {bound.ProductId}.");
        }

        var relationship = bound;
        if (relationship is null)
        {
            relationship = new FirmProductRelationship(request.FirmId, request.ProductId, request.AcquisitionDate,
                request.PricePaidPerPeriod?.ToDomain())
            {
                Notes = request.Notes
            };
            relationship.SetServedProcesses(request.ServedProcessIds ?? []);
            relationship.SetPriceLevel(request.PriceLevel);
            if (request.EndDate is { } end) relationship.End(end);
            db.FirmProductRelationships.Add(relationship);
        }

        for (var i = 0; i < sources.Count; i++)
            if (bindings[i]?.TargetId != relationship.Id)
                db.Bind(bindings[i], workspaceId, sources[i], nameof(FirmProductRelationship), relationship.Id);

        await db.SaveChangesAsync(ct);
        if (bound is not null) return Ok(bound.ToDto());
        return CreatedAtAction(nameof(Get), new { id = relationship.Id }, relationship.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FirmProductRelationshipDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FirmProductRelationshipDto>> Update(Guid id,
        [FromBody] UpdateFirmProductRelationshipRequest request, CancellationToken ct)
    {
        var relationship = await db.FirmProductRelationships.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (relationship is null) return NotFoundProblem("FirmProductRelationship", id);

        relationship.Reopen();
        relationship.SetAcquisitionDate(request.AcquisitionDate);
        if (request.EndDate is { } end) relationship.End(end);
        relationship.SetServedProcesses(request.ServedProcessIds ?? []);
        relationship.SetPriceLevel(request.PriceLevel);
        relationship.SetPricePaid(request.PricePaidPerPeriod?.ToDomain());
        relationship.Notes = request.Notes;

        await db.SaveChangesAsync(ct);
        return Ok(relationship.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var relationship = await db.FirmProductRelationships.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (relationship is null) return NotFoundProblem("FirmProductRelationship", id);

        db.FirmProductRelationships.Remove(relationship);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
