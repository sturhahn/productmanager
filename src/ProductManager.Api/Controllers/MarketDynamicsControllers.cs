using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Market participants: prospects, our clients, competitors' clients, and synthetic firms generated
/// to fill the unobserved market. The epistemic status says which kind of knowledge a row is.
/// </summary>
[Route("api/firms")]
public sealed class FirmsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<FirmDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<FirmDto>> List(
        [FromQuery] Guid? marketId,
        [FromQuery] Guid? clientSegmentId,
        [FromQuery] EpistemicStatus? epistemicStatus,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Firms.AsNoTracking()
            .Where(f => (marketId == null || f.MarketId == marketId)
                        && (clientSegmentId == null || f.ClientSegmentId == clientSegmentId)
                        && (epistemicStatus == null || f.EpistemicStatus == epistemicStatus))
            .OrderBy(f => f.Name)
            .ToPagedResponseAsync(paging, MarketDynamicsMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FirmDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FirmDto>> Get(Guid id, CancellationToken ct)
    {
        var firm = await db.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return firm is null ? NotFoundProblem("Firm", id) : Ok(firm.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(FirmDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<FirmDto>> Create([FromBody] CreateFirmRequest request, CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);
        if (request.ClientSegmentId is not null &&
            !await db.ClientSegments.AnyAsync(s => s.Id == request.ClientSegmentId, ct))
            return NotFoundProblem("ClientSegment", request.ClientSegmentId.Value);

        var firm = new Firm(request.MarketId, request.Name, request.EpistemicStatus, request.ClientSegmentId)
        {
            AttributesJson = request.AttributesJson,
            Notes = request.Notes
        };
        db.Firms.Add(firm);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = firm.Id }, firm.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FirmDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FirmDto>> Update(Guid id, [FromBody] UpdateFirmRequest request, CancellationToken ct)
    {
        var firm = await db.Firms.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (firm is null) return NotFoundProblem("Firm", id);
        if (request.ClientSegmentId is not null &&
            !await db.ClientSegments.AnyAsync(s => s.Id == request.ClientSegmentId, ct))
            return NotFoundProblem("ClientSegment", request.ClientSegmentId.Value);

        firm.Rename(request.Name);
        firm.SetEpistemicStatus(request.EpistemicStatus);
        firm.ClientSegmentId = request.ClientSegmentId;
        firm.AttributesJson = request.AttributesJson;
        firm.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(firm.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var firm = await db.Firms.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (firm is null) return NotFoundProblem("Firm", id);

        db.Firms.Remove(firm);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Imports a whole roster into one market in a single call, relationships included. Matching is
    /// by natural key (market, name), so re-importing the same roster corrects it rather than
    /// duplicating it — the same idempotence the seed loader relies on.
    /// </summary>
    [HttpPost("bulk")]
    [ProducesResponseType(typeof(BulkImportFirmsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BulkImportFirmsResponse>> BulkImport([FromBody] BulkImportFirmsRequest request,
        CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);

        var segmentIds = await db.ClientSegments.AsNoTracking()
            .Where(s => s.MarketId == request.MarketId)
            .Select(s => s.Id)
            .ToListAsync(ct);
        var productIds = await db.Products.AsNoTracking()
            .Where(p => p.MarketId == request.MarketId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var item in request.Firms)
        {
            if (item.ClientSegmentId is not null && !segmentIds.Contains(item.ClientSegmentId.Value))
                return NotFoundProblem("ClientSegment", item.ClientSegmentId.Value);
            foreach (var relationship in item.Relationships ?? [])
                if (!productIds.Contains(relationship.ProductId))
                    return NotFoundProblem("Product", relationship.ProductId);
        }

        var existing = await db.Firms.Where(f => f.MarketId == request.MarketId).ToListAsync(ct);
        var byName = existing
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int created = 0, updated = 0, relationshipsWritten = 0;
        var touched = new List<Firm>();

        foreach (var item in request.Firms)
        {
            string name = item.Name.Trim();
            if (!byName.TryGetValue(name, out var firm))
            {
                firm = new Firm(request.MarketId, name, item.EpistemicStatus, item.ClientSegmentId);
                db.Firms.Add(firm);
                byName[name] = firm;
                created++;
            }
            else
            {
                firm.Rename(name);
                firm.SetEpistemicStatus(item.EpistemicStatus);
                firm.ClientSegmentId = item.ClientSegmentId;
                updated++;
            }

            firm.AttributesJson = item.AttributesJson;
            firm.Notes = item.Notes;
            touched.Add(firm);

            foreach (var incoming in item.Relationships ?? [])
            {
                var relationship = await db.FirmProductRelationships
                    .FirstOrDefaultAsync(r => r.FirmId == firm.Id && r.ProductId == incoming.ProductId, ct);
                if (relationship is null)
                {
                    relationship = new FirmProductRelationship(firm.Id, incoming.ProductId, incoming.AcquisitionDate,
                        incoming.PricePaidPerPeriod?.ToDomain());
                    db.FirmProductRelationships.Add(relationship);
                }
                else
                {
                    relationship.SetAcquisitionDate(incoming.AcquisitionDate);
                    relationship.SetPricePaid(incoming.PricePaidPerPeriod?.ToDomain());
                }

                relationship.SetServedProcesses(incoming.ServedProcessIds ?? []);
                relationship.SetPriceLevel(incoming.PriceLevel);
                relationshipsWritten++;
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new BulkImportFirmsResponse(request.MarketId, created, updated, relationshipsWritten,
            touched.Select(MarketDynamicsMapping.ToDto).ToList()));
    }

    /// <summary>
    /// Fills every segment's measured stocks with deterministically generated synthetic firms, so a
    /// firm-level simulation runs over the whole market and not only the part we happen to have
    /// names for. Regeneration <em>replaces</em> the previous synthetic population: identities are
    /// hashes of (market, segment, role, index), so running this twice is a no-op rather than a
    /// doubling.
    /// </summary>
    [HttpPost("generate-synthetic")]
    [ProducesResponseType(typeof(GenerateSyntheticFirmsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerateSyntheticFirmsResponse>> GenerateSynthetic(
        [FromBody] GenerateSyntheticFirmsRequest request, CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);

        var segments = await db.ClientSegments
            .Where(s => s.MarketId == request.MarketId)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var products = await db.Products
            .Where(p => p.MarketId == request.MarketId)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        // Clear the previous generation first: a synthetic population is a derived artifact of the
        // stocks, not an accumulating roster.
        var previous = await db.Firms
            .Where(f => f.MarketId == request.MarketId && f.EpistemicStatus == EpistemicStatus.Synthetic)
            .ToListAsync(ct);
        var previousIds = previous.Select(f => f.Id).ToList();
        var previousRelationships = await db.FirmProductRelationships
            .Where(r => previousIds.Contains(r.FirmId))
            .ToListAsync(ct);
        db.FirmProductRelationships.RemoveRange(previousRelationships);
        db.Firms.RemoveRange(previous);
        await db.SaveChangesAsync(ct);

        var observed = await db.Firms.AsNoTracking()
            .Where(f => f.MarketId == request.MarketId && f.EpistemicStatus != EpistemicStatus.Synthetic)
            .ToListAsync(ct);
        var observedIds = observed.Select(f => f.Id).ToList();

        var ourProductIds = products.Where(p => p.IsOurs).Select(p => p.Id).ToList();
        var clientFirmIds = await db.FirmProductRelationships.AsNoTracking()
            .Where(r => r.EndDate == null && observedIds.Contains(r.FirmId) && ourProductIds.Contains(r.ProductId))
            .Select(r => r.FirmId)
            .ToListAsync(ct);
        var clientFirmIdSet = clientFirmIds.ToHashSet();

        var fills = new List<SyntheticSegmentFill>();
        foreach (var segment in segments)
        {
            var flagship = products.FirstOrDefault(p =>
                p.IsOurs && !p.IsModule
                && p.Prices.Any(price => price.ClientSegmentId == segment.Id || price.ClientSegmentId is null));

            var modules = segment.ModulePenetrations
                .OrderBy(m => m.ModuleProductId)
                .Select(m => (Module: products.FirstOrDefault(p => p.Id == m.ModuleProductId), m.Penetration))
                .Where(m => m.Module is not null && m.Module.IsOurs)
                .Select(m => (Module: m.Module!, m.Penetration))
                .ToList();

            fills.Add(SyntheticFirmGenerator.Generate(request.MarketId, new SyntheticFirmGenerator.SegmentInputs(
                segment,
                observed.Where(f => f.ClientSegmentId == segment.Id).ToList(),
                clientFirmIdSet,
                flagship,
                modules)));
        }

        foreach (var fill in fills)
        {
            db.Firms.AddRange(fill.Firms);
            db.FirmProductRelationships.AddRange(fill.Relationships);
        }

        await db.SaveChangesAsync(ct);

        return Ok(new GenerateSyntheticFirmsResponse(
            request.MarketId,
            previous.Count,
            fills.Sum(f => f.Firms.Count),
            fills.Sum(f => f.Relationships.Count),
            fills.Select(f => new SyntheticSegmentFillDto(
                f.ClientSegmentId, f.ProspectTarget, f.ObservedProspects, f.SyntheticProspects,
                f.ClientTarget, f.ObservedClients, f.SyntheticClients)).ToList()));
    }
}

/// <summary>
/// The demand edge: what a firm — or a whole segment — must accomplish, how much it matters, and
/// what the workaround costs when it goes unmet.
/// </summary>
[Route("api/needs")]
public sealed class NeedsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<NeedDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<NeedDto>> List(
        [FromQuery] Guid? firmId,
        [FromQuery] Guid? clientSegmentId,
        [FromQuery] Guid? businessProcessId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Needs.AsNoTracking()
            .Where(n => (firmId == null || n.FirmId == firmId)
                        && (clientSegmentId == null || n.ClientSegmentId == clientSegmentId)
                        && (businessProcessId == null || n.BusinessProcessId == businessProcessId))
            .OrderByDescending(n => n.ImportanceWeight).ThenBy(n => n.Id)
            .ToPagedResponseAsync(paging, MarketDynamicsMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NeedDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NeedDto>> Get(Guid id, CancellationToken ct)
    {
        var need = await db.Needs.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        return need is null ? NotFoundProblem("Need", id) : Ok(need.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(NeedDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<NeedDto>> Create([FromBody] CreateNeedRequest request, CancellationToken ct)
    {
        if ((request.FirmId is null) == (request.ClientSegmentId is null))
            return Problem(detail: "A need belongs to exactly one of a firm or a segment.",
                statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == request.BusinessProcessId, ct))
            return NotFoundProblem("BusinessProcess", request.BusinessProcessId);
        if (request.FirmId is not null && !await db.Firms.AnyAsync(f => f.Id == request.FirmId, ct))
            return NotFoundProblem("Firm", request.FirmId.Value);
        if (request.ClientSegmentId is not null &&
            !await db.ClientSegments.AnyAsync(s => s.Id == request.ClientSegmentId, ct))
            return NotFoundProblem("ClientSegment", request.ClientSegmentId.Value);

        var workaround = request.WorkaroundCostPerPeriod?.ToDomain();
        var need = request.FirmId is not null
            ? Need.ForFirm(request.FirmId.Value, request.BusinessProcessId, request.ImportanceWeight,
                request.IsHard, workaround)
            : Need.ForSegment(request.ClientSegmentId!.Value, request.BusinessProcessId, request.ImportanceWeight,
                request.IsHard, workaround);

        need.RequiredDepthJson = request.RequiredDepthJson;
        need.Conditionality = request.Conditionality;
        need.Notes = request.Notes;

        db.Needs.Add(need);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = need.Id }, need.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(NeedDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NeedDto>> Update(Guid id, [FromBody] UpdateNeedRequest request, CancellationToken ct)
    {
        var need = await db.Needs.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (need is null) return NotFoundProblem("Need", id);

        need.SetImportance(request.ImportanceWeight);
        need.SetHardness(request.IsHard, request.WorkaroundCostPerPeriod?.ToDomain());
        need.RequiredDepthJson = request.RequiredDepthJson;
        need.Conditionality = request.Conditionality;
        need.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(need.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var need = await db.Needs.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (need is null) return NotFoundProblem("Need", id);

        db.Needs.Remove(need);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>
/// Typed shocks that modulate simulated flows for a duration. Real events mirror the facts ledger;
/// hypothetical ones are injected into a scenario — "with the incident" vs "without".
/// </summary>
[Route("api/market-events")]
public sealed class MarketEventsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<MarketEventDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<MarketEventDto>> List(
        [FromQuery] Guid? marketId,
        [FromQuery] bool? isHypothetical,
        [FromQuery] Guid? competitorId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.MarketEvents.AsNoTracking()
            .Where(e => (marketId == null || e.MarketId == marketId)
                        && (isHypothetical == null || e.IsHypothetical == isHypothetical)
                        && (competitorId == null || e.CompetitorId == competitorId))
            .OrderBy(e => e.StartDate).ThenBy(e => e.Name)
            .ToPagedResponseAsync(paging, MarketDynamicsMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MarketEventDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketEventDto>> Get(Guid id, CancellationToken ct)
    {
        var marketEvent = await db.MarketEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return marketEvent is null ? NotFoundProblem("MarketEvent", id) : Ok(marketEvent.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(MarketEventDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<MarketEventDto>> Create([FromBody] CreateMarketEventRequest request,
        CancellationToken ct)
    {
        if (!await db.Markets.AnyAsync(m => m.Id == request.MarketId, ct))
            return NotFoundProblem("Market", request.MarketId);
        if (request.CompetitorId is not null && !await db.Competitors.AnyAsync(c => c.Id == request.CompetitorId, ct))
            return NotFoundProblem("Competitor", request.CompetitorId.Value);

        var marketEvent = new MarketEvent(request.MarketId, request.Type, request.Name, request.StartDate,
            request.IsHypothetical, request.EndDate)
        {
            Magnitude = request.Magnitude,
            CompetitorId = request.CompetitorId,
            Notes = request.Notes
        };
        marketEvent.ReplaceRateMultipliers(request.RateMultipliers.ToDomain());

        db.MarketEvents.Add(marketEvent);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = marketEvent.Id }, marketEvent.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(MarketEventDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketEventDto>> Update(Guid id, [FromBody] UpdateMarketEventRequest request,
        CancellationToken ct)
    {
        var marketEvent = await db.MarketEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (marketEvent is null) return NotFoundProblem("MarketEvent", id);
        if (request.CompetitorId is not null && !await db.Competitors.AnyAsync(c => c.Id == request.CompetitorId, ct))
            return NotFoundProblem("Competitor", request.CompetitorId.Value);

        marketEvent.Rename(request.Name);
        marketEvent.SetType(request.Type);
        marketEvent.Reschedule(request.StartDate, request.EndDate);
        marketEvent.SetHypothetical(request.IsHypothetical);
        marketEvent.Magnitude = request.Magnitude;
        marketEvent.CompetitorId = request.CompetitorId;
        marketEvent.Notes = request.Notes;
        marketEvent.ReplaceRateMultipliers(request.RateMultipliers.ToDomain());

        await db.SaveChangesAsync(ct);
        return Ok(marketEvent.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var marketEvent = await db.MarketEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (marketEvent is null) return NotFoundProblem("MarketEvent", id);

        db.MarketEvents.Remove(marketEvent);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
