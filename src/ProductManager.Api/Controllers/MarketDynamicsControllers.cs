using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
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
