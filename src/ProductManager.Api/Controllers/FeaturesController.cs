using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Common;
using ProductManager.Domain.Seam;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Product capabilities (not work items). A feature's market meaning is its set of deltas —
/// mostly coverage deltas ("bronze → gold on process P for segment S").
/// </summary>
[Route("api/features")]
public sealed class FeaturesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<FeatureDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<FeatureDto>> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? productId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Features.AsNoTracking()
            .Where(f => (workspaceId == null || f.WorkspaceId == workspaceId)
                        && (productId == null || f.ProductId == productId))
            .OrderBy(f => f.Name)
            .ToPagedResponseAsync(paging, SeamMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FeatureDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FeatureDto>> Get(Guid id, CancellationToken ct)
    {
        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return feature is null ? NotFoundProblem("Feature", id) : Ok(feature.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(FeatureDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<FeatureDto>> Create([FromBody] CreateFeatureRequest request, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return NotFoundProblem("Workspace", request.WorkspaceId);

        var feature = new Feature(request.WorkspaceId, request.Name, request.ProductId)
        {
            Description = request.Description
        };

        db.Features.Add(feature);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = feature.Id }, feature.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FeatureDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FeatureDto>> Update(Guid id, [FromBody] UpdateFeatureRequest request,
        CancellationToken ct)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null) return NotFoundProblem("Feature", id);

        feature.Rename(request.Name);
        feature.Description = request.Description;
        feature.ProductId = request.ProductId;

        switch (request.Status)
        {
            case FeatureStatus.Planned:
                feature.MarkPlanned();
                break;
            case FeatureStatus.Shipped:
                if (request.ShippedDate is null)
                    return Problem(detail: "A shipped feature requires a shippedDate.",
                        statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");
                feature.MarkShipped(request.ShippedDate.Value);
                break;
        }

        await db.SaveChangesAsync(ct);
        return Ok(feature.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null) return NotFoundProblem("Feature", id);

        db.Features.Remove(feature);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Adds one delta to the feature's market meaning.</summary>
    [HttpPost("{id:guid}/deltas")]
    [ProducesResponseType(typeof(FeatureDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FeatureDto>> AddDelta(Guid id, [FromBody] AddFeatureDeltaRequest request,
        CancellationToken ct)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null) return NotFoundProblem("Feature", id);

        AddDeltaCore(feature, request);
        await db.SaveChangesAsync(ct);
        return Ok(feature.ToDto());
    }

    /// <summary>Replaces the feature's whole delta set.</summary>
    [HttpPut("{id:guid}/deltas")]
    [ProducesResponseType(typeof(FeatureDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FeatureDto>> ReplaceDeltas(Guid id,
        [FromBody] IReadOnlyList<AddFeatureDeltaRequest> requests, CancellationToken ct)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null) return NotFoundProblem("Feature", id);

        foreach (var existing in feature.Deltas.ToList())
            feature.RemoveDelta(existing);

        foreach (var request in requests)
            AddDeltaCore(feature, request);

        await db.SaveChangesAsync(ct);
        return Ok(feature.ToDto());
    }

    private static void AddDeltaCore(Feature feature, AddFeatureDeltaRequest request)
    {
        if (request.Kind == FeatureDeltaKind.Coverage)
        {
            if (request.BusinessProcessId is null || request.ToTier is null)
                throw new ArgumentException("A coverage delta requires businessProcessId and toTier.");
            feature.AddCoverageDelta(request.BusinessProcessId.Value, request.FromTier, request.ToTier.Value,
                request.ApproachId, request.SegmentId, request.Rationale);
        }
        else
        {
            if (request.Magnitude is null)
                throw new ArgumentException($"A {request.Kind} delta requires a magnitude.");
            feature.AddOtherDelta(request.Kind, request.Magnitude.Value, request.BusinessProcessId,
                request.SegmentId, request.Rationale);
        }
    }
}
