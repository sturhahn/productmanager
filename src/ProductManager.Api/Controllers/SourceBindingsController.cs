using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Import provenance, read-only: which source record became which row. Bindings are written by the
/// import paths themselves (firm bulk import, opportunity/relationship/fact creates with a source);
/// this listing is what an importer resolves and plans against, and what reconciliation traces a
/// row back to its source record with.
/// </summary>
[Route("api/source-bindings")]
public sealed class SourceBindingsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<SourceBindingDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<SourceBindingDto>> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? sourceSystem,
        [FromQuery] string? sourceInstance,
        [FromQuery] string? objectKind,
        [FromQuery] string? sourceRecordId,
        [FromQuery] string? targetType,
        [FromQuery] Guid? targetId,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.SourceBindings.AsNoTracking()
            .Where(b => (workspaceId == null || b.WorkspaceId == workspaceId)
                        && (sourceSystem == null || b.SourceSystem == sourceSystem)
                        && (sourceInstance == null || b.SourceInstance == sourceInstance)
                        && (objectKind == null || b.ObjectKind == objectKind)
                        && (sourceRecordId == null || b.SourceRecordId == sourceRecordId)
                        && (targetType == null || b.TargetType == targetType)
                        && (targetId == null || b.TargetId == targetId))
            .OrderBy(b => b.SourceSystem).ThenBy(b => b.SourceInstance).ThenBy(b => b.ObjectKind)
            .ThenBy(b => b.SourceRecordId).ThenBy(b => b.Id)
            .ToPagedResponseAsync(paging, SourceMapping.ToDto, ct);
}
