using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Domain.Facts;
using ProductManager.Domain.Imports;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Controllers;

/// <summary>
/// The central hinge of the market model: a firm seeking coverage for a scoped set of business
/// processes. Scope can expand mid-cycle (land-and-expand); the lifecycle is open → qualified →
/// decided (won / lost / no-decision), and "no decision" is a real outcome rather than a missing one.
///
/// <para>
/// Deciding an opportunity also appends an <see cref="FactEventKind.OpportunityDecided"/> entry to
/// the bi-temporal ledger, carrying a self-contained copy of the deal. Calibration reads that
/// ledger, never these rows: a fit "as of date D" must see what was recorded by D, and an
/// opportunity row keeps changing afterwards.
/// </para>
/// </summary>
[Route("api/opportunities")]
public sealed class OpportunitiesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OpportunityDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<OpportunityDto>> List(
        [FromQuery] Guid? firmId,
        [FromQuery] Guid? channelId,
        [FromQuery] OpportunityStage? stage,
        [FromQuery] bool? isCrossSell,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Opportunities.AsNoTracking()
            .Where(o => (firmId == null || o.FirmId == firmId)
                        && (channelId == null || o.ChannelId == channelId)
                        && (stage == null || o.Stage == stage)
                        && (isCrossSell == null
                            || (isCrossSell == true
                                ? o.CrossSellModuleProductId != null
                                : o.CrossSellModuleProductId == null)))
            .OrderByDescending(o => o.OpenedAt)
            .ToPagedResponseAsync(paging, Phase3Mapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpportunityDto>> Get(Guid id, CancellationToken ct)
    {
        var opportunity = await db.Opportunities.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        return opportunity is null ? NotFoundProblem("Opportunity", id) : Ok(opportunity.ToDto());
    }

    /// <summary>
    /// Opens an opportunity. With a <c>source</c> already bound, returns the bound opportunity (200)
    /// instead — the unique binding index makes this safe under retries.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OpportunityDto>> Create([FromBody] CreateOpportunityRequest request,
        CancellationToken ct)
    {
        var firm = await db.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FirmId, ct);
        if (firm is null) return NotFoundProblem("Firm", request.FirmId);

        Guid workspaceId = default;
        SourceBinding? binding = null;
        if (request.Source is not null)
        {
            workspaceId = await db.Markets.Where(m => m.Id == firm.MarketId).Select(m => m.WorkspaceId).FirstAsync(ct);
            binding = await db.FindBindingAsync(workspaceId, request.Source, ct);
            if (binding is not null && binding.TargetType != nameof(Opportunity))
                return ConflictProblem($"Source record '{request.Source.SourceRecordId}' is bound to a {binding.TargetType}.");
            var bound = binding is null
                ? null
                : await db.Opportunities.AsNoTracking().FirstOrDefaultAsync(o => o.Id == binding.TargetId, ct);
            if (bound is not null)
                return bound.FirmId == request.FirmId
                    ? Ok(bound.ToDto())
                    : CorrectionRequiredProblem(
                        $"Source record '{request.Source.SourceRecordId}' is already opportunity {bound.Id} " +
                        $"at firm {bound.FirmId}, not firm {request.FirmId}.");
        }
        if (request.ChannelId is not null && !await db.Channels.AnyAsync(c => c.Id == request.ChannelId, ct))
            return NotFoundProblem("Channel", request.ChannelId.Value);
        if (request.CrossSellModuleProductId is not null &&
            !await db.Products.AnyAsync(p => p.Id == request.CrossSellModuleProductId, ct))
            return NotFoundProblem("Product", request.CrossSellModuleProductId.Value);

        var scope = request.ScopeProcessIds ?? [];
        foreach (var processId in scope)
            if (!await db.BusinessProcesses.AnyAsync(p => p.Id == processId, ct))
                return NotFoundProblem("BusinessProcess", processId);

        var openedAt = request.OpenedAt ?? DateTimeOffset.UtcNow;
        var opportunity = new Opportunity(request.FirmId, openedAt, request.ChannelId,
            request.CrossSellModuleProductId)
        {
            Notes = request.Notes
        };
        opportunity.SetCompetitors(request.CompetitorIds ?? []);

        foreach (var processId in scope.Distinct())
            opportunity.AddToScope(processId, openedAt);

        db.Opportunities.Add(opportunity);
        if (request.Source is not null)
            db.Bind(binding, workspaceId, request.Source, nameof(Opportunity), opportunity.Id);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = opportunity.Id }, opportunity.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OpportunityDto>> Update(Guid id, [FromBody] UpdateOpportunityRequest request,
        CancellationToken ct)
    {
        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null) return NotFoundProblem("Opportunity", id);
        if (request.ChannelId is not null && !await db.Channels.AnyAsync(c => c.Id == request.ChannelId, ct))
            return NotFoundProblem("Channel", request.ChannelId.Value);
        if (request.CrossSellModuleProductId is not null &&
            !await db.Products.AnyAsync(p => p.Id == request.CrossSellModuleProductId, ct))
            return NotFoundProblem("Product", request.CrossSellModuleProductId.Value);

        opportunity.ChannelId = request.ChannelId;
        opportunity.SetCrossSellModule(request.CrossSellModuleProductId);
        opportunity.SetCompetitors(request.CompetitorIds ?? []);
        opportunity.Notes = request.Notes;

        await db.SaveChangesAsync(ct);
        return Ok(opportunity.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null) return NotFoundProblem("Opportunity", id);

        db.Opportunities.Remove(opportunity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Adds a business process to the scope (idempotent). Refused once the deal is decided.</summary>
    [HttpPost("{id:guid}/scope")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OpportunityDto>> AddScope(Guid id, [FromBody] AddOpportunityScopeRequest request,
        CancellationToken ct)
    {
        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null) return NotFoundProblem("Opportunity", id);
        if (!await db.BusinessProcesses.AnyAsync(p => p.Id == request.BusinessProcessId, ct))
            return NotFoundProblem("BusinessProcess", request.BusinessProcessId);

        opportunity.AddToScope(request.BusinessProcessId, request.AddedAt ?? DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Ok(opportunity.ToDto());
    }

    [HttpDelete("{id:guid}/scope/{businessProcessId:guid}")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OpportunityDto>> RemoveScope(Guid id, Guid businessProcessId, CancellationToken ct)
    {
        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null) return NotFoundProblem("Opportunity", id);
        if (!opportunity.RemoveFromScope(businessProcessId))
            return NotFoundProblem("Scope item for process", businessProcessId);

        await db.SaveChangesAsync(ct);
        return Ok(opportunity.ToDto());
    }

    /// <summary>Qualifies the opportunity; repeating the same qualification is a 200 no-op.</summary>
    [HttpPost("{id:guid}/qualify")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OpportunityDto>> Qualify(Guid id, [FromBody] QualifyOpportunityRequest request,
        CancellationToken ct)
    {
        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null) return NotFoundProblem("Opportunity", id);

        if (opportunity.QualifiedAt is { } qualifiedAt && request.At is { } requested
            && !Opportunity.SameInstant(qualifiedAt, requested))
            return CorrectionRequiredProblem(
                $"Opportunity {id} was already qualified on {qualifiedAt:O}; the recorded date is not rewritten.");

        // An omitted date on a retry means "the qualification you already have".
        var at = request.At ?? opportunity.QualifiedAt ?? DateTimeOffset.UtcNow;
        if (!opportunity.Qualify(at)) return Ok(opportunity.ToDto());
        await db.SaveChangesAsync(ct);
        return Ok(opportunity.ToDto());
    }

    /// <summary>
    /// Decides the opportunity and appends the ledger entry that calibration will later read. The
    /// two writes are one transaction: a decision that never reached the ledger would silently
    /// disappear from every future fit.
    ///
    /// <para>
    /// Repeat-safe: the identical decision again (a retry after a lost response) returns 200 and
    /// appends nothing; a different one is refused as a correction, leaving the recorded decision and
    /// its fact untouched.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/decide")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OpportunityDto>> Decide(Guid id, [FromBody] DecideOpportunityRequest request,
        CancellationToken ct)
    {
        var opportunity = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opportunity is null) return NotFoundProblem("Opportunity", id);

        if (opportunity.IsDecided)
            return opportunity.IsSameDecision(request.Outcome, request.At ?? opportunity.DecidedAt!.Value,
                request.WinningProductId)
                ? Ok(opportunity.ToDto())
                : CorrectionRequiredProblem(
                    $"Opportunity {id} is already decided ({opportunity.Stage} on {opportunity.DecidedAt:O}). " +
                    "Changing a recorded decision is a correction of the ledger, which this API does not rewrite.");

        var firm = await db.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == opportunity.FirmId, ct);
        if (firm is null) return NotFoundProblem("Firm", opportunity.FirmId);

        Guid? winnerCompetitorId = null;
        if (request.WinningProductId is not null)
        {
            var product = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.WinningProductId, ct);
            if (product is null) return NotFoundProblem("Product", request.WinningProductId.Value);
            winnerCompetitorId = product.CompetitorId;
        }

        if (!TryResolveRecordedDate(request.RecordedAt, out var recordedDate, out string? error))
            return BadRequestProblem(error!);

        var at = request.At ?? DateTimeOffset.UtcNow;
        opportunity.Decide(request.Outcome, at, request.WinningProductId);

        var market = await db.Markets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == firm.MarketId, ct);
        if (market is null) return NotFoundProblem("Market", firm.MarketId);

        bool winnerIsOurs = request.WinningProductId is not null && winnerCompetitorId is null;
        string outcome = request.Outcome switch
        {
            OpportunityStage.Won when winnerIsOurs => OpportunityDecisionFact.WonOutcome,
            OpportunityStage.Won => OpportunityDecisionFact.LostOutcome, // a competitor's product won it
            OpportunityStage.Lost => OpportunityDecisionFact.LostOutcome,
            _ => OpportunityDecisionFact.NoDecisionOutcome
        };

        var payload = new OpportunityDecisionFact(
            opportunity.Id,
            opportunity.FirmId,
            firm.ClientSegmentId,
            opportunity.ChannelId,
            DateOnly.FromDateTime(opportunity.OpenedAt.UtcDateTime),
            DateOnly.FromDateTime(at.UtcDateTime),
            outcome,
            opportunity.Scope.Select(s => s.BusinessProcessId).OrderBy(p => p).ToList(),
            opportunity.WinningProductId,
            winnerIsOurs,
            opportunity.IsCrossSell,
            opportunity.CrossSellModuleProductId);

        // One kind for every decision, won or not: the outcome lives in the payload, so a fit reads
        // wins and losses out of the same stream and cannot accidentally count only the wins.
        db.FactEvents.Add(new FactEvent(
            market.WorkspaceId,
            FactEventKind.OpportunityDecided,
            DateOnly.FromDateTime(at.UtcDateTime),
            recordedDate,
            nameof(Opportunity),
            opportunity.Id,
            SimulationJson.Serialize(payload),
            $"Opportunity decided: {outcome}."));

        await db.SaveChangesAsync(ct);
        return Ok(opportunity.ToDto());
    }
}
