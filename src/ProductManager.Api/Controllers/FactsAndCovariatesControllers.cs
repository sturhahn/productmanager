using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Domain.Facts;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Controllers;

/// <summary>
/// The bi-temporal facts ledger: every market observation, with when it was true and when we
/// learned it. Append-only by construction — there is no update and no delete, because rewriting
/// history is exactly what would make "what we knew as of date D" a lie.
/// </summary>
[Route("api/facts")]
public sealed class FactsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<FactEventDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<FactEventDto>> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] FactEventKind? kind,
        [FromQuery] string? subjectType,
        [FromQuery] Guid? subjectId,
        [FromQuery] DateOnly? effectiveFrom,
        [FromQuery] DateOnly? effectiveTo,
        [FromQuery] DateTimeOffset? knownAsOf,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.FactEvents.AsNoTracking()
            .Where(f => (workspaceId == null || f.WorkspaceId == workspaceId)
                        && (kind == null || f.Kind == kind)
                        && (subjectType == null || f.SubjectType == subjectType)
                        && (subjectId == null || f.SubjectId == subjectId)
                        && (effectiveFrom == null || f.EffectiveDate >= effectiveFrom)
                        && (effectiveTo == null || f.EffectiveDate <= effectiveTo)
                        && (knownAsOf == null || f.RecordedDate <= knownAsOf))
            .OrderByDescending(f => f.EffectiveDate).ThenByDescending(f => f.RecordedDate)
            .ToPagedResponseAsync(paging, FactMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FactEventDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FactEventDto>> Get(Guid id, CancellationToken ct)
    {
        var fact = await db.FactEvents.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return fact is null ? NotFoundProblem("FactEvent", id) : Ok(fact.ToDto());
    }

    /// <summary>
    /// Appends one observation. The recorded date is stamped server-side unless the caller supplies
    /// <c>recordedAt</c> — the import/backfill path, for history whose "when we learned it" is
    /// genuinely in the past. A recorded date in the future is refused.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FactEventDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<FactEventDto>> Create([FromBody] CreateFactEventRequest request,
        CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return NotFoundProblem("Workspace", request.WorkspaceId);
        if (!IsJsonOrEmpty(request.PayloadJson))
            return BadRequestProblem("payloadJson must be a JSON document.");
        if (request.Kind == FactEventKind.CovariateObserved
            && !CovariateHistoryReader.TryReadValue(request.PayloadJson, out _))
            return BadRequestProblem("A CovariateObserved fact needs a payload of {\"value\": <number>}.");
        if (!TryResolveRecordedDate(request.RecordedAt, out var recordedDate, out string? error))
            return BadRequestProblem(error!);

        var fact = new FactEvent(request.WorkspaceId, request.Kind, request.EffectiveDate, recordedDate,
            request.SubjectType, request.SubjectId, request.PayloadJson, request.Notes);

        db.FactEvents.Add(fact);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = fact.Id }, fact.ToDto());
    }

    private static bool IsJsonOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// Named external time series that drive flows — an equity index, a rate, a volume. Observed
/// history lives in the facts ledger; the future is a scenario spec sampled per Monte Carlo path.
/// </summary>
[Route("api/covariates")]
public sealed class CovariatesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<CovariateDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<CovariateDto>> List([FromQuery] Guid? workspaceId, [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Covariates.AsNoTracking()
            .Where(c => workspaceId == null || c.WorkspaceId == workspaceId)
            .OrderBy(c => c.Name)
            .ToPagedResponseAsync(paging, MarketDynamicsMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CovariateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CovariateDto>> Get(Guid id, CancellationToken ct)
    {
        var covariate = await db.Covariates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return covariate is null ? NotFoundProblem("Covariate", id) : Ok(covariate.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(CovariateDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CovariateDto>> Create([FromBody] CreateCovariateRequest request,
        CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return NotFoundProblem("Workspace", request.WorkspaceId);

        var covariate = new Covariate(request.WorkspaceId, request.Name, request.Unit)
        {
            Description = request.Description
        };
        covariate.SetGbmSpec(request.GbmStart, request.GbmDrift, request.GbmVol);

        db.Covariates.Add(covariate);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = covariate.Id }, covariate.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CovariateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CovariateDto>> Update(Guid id, [FromBody] UpdateCovariateRequest request,
        CancellationToken ct)
    {
        var covariate = await db.Covariates.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (covariate is null) return NotFoundProblem("Covariate", id);

        covariate.Rename(request.Name);
        covariate.Description = request.Description;
        covariate.Unit = request.Unit;
        covariate.SetGbmSpec(request.GbmStart, request.GbmDrift, request.GbmVol);
        await db.SaveChangesAsync(ct);
        return Ok(covariate.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var covariate = await db.Covariates.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (covariate is null) return NotFoundProblem("Covariate", id);

        db.Covariates.Remove(covariate);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// The covariate's observed history, read out of the facts ledger. <c>knownAsOf</c> reconstructs
    /// what we knew at a past instant; where a reading was later corrected for the same effective
    /// date, the latest recorded value wins.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(CovariateHistoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CovariateHistoryDto>> History(
        Guid id,
        [FromQuery] DateOnly? effectiveFrom,
        [FromQuery] DateOnly? effectiveTo,
        [FromQuery] DateTimeOffset? knownAsOf,
        CancellationToken ct)
    {
        var covariate = await db.Covariates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (covariate is null) return NotFoundProblem("Covariate", id);

        var facts = await db.FactEvents.AsNoTracking()
            .Where(f => f.Kind == FactEventKind.CovariateObserved
                        && f.SubjectType == FactMapping.CovariateSubjectType
                        && f.SubjectId == id
                        && (effectiveFrom == null || f.EffectiveDate >= effectiveFrom)
                        && (effectiveTo == null || f.EffectiveDate <= effectiveTo)
                        && (knownAsOf == null || f.RecordedDate <= knownAsOf))
            .ToListAsync(ct);

        // The DTO keeps the fact id and recorded date so a reading always traces back to its ledger
        // entry; ToObservations collapses corrections, so re-find the winning fact for each date.
        var winning = facts
            .GroupBy(f => f.EffectiveDate)
            .Select(g => g.OrderByDescending(f => f.RecordedDate).First())
            .Where(f => CovariateHistoryReader.TryReadValue(f.PayloadJson, out _))
            .OrderBy(f => f.EffectiveDate)
            .Select(f =>
            {
                CovariateHistoryReader.TryReadValue(f.PayloadJson, out double value);
                return new CovariateObservationDto(f.Id, f.EffectiveDate, f.RecordedDate, value, f.Notes);
            })
            .ToList();

        return Ok(new CovariateHistoryDto(covariate.Id, covariate.Name, covariate.Unit, winning));
    }
}
