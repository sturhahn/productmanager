using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Execution;

namespace ProductManager.Api.Controllers;

/// <summary>The root aggregate. Phase 1 auto-creates a single default workspace on first use.</summary>
[Route("api/workspaces")]
public sealed class WorkspacesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<WorkspaceDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<WorkspaceDto>> List([FromQuery] PageQuery paging, CancellationToken ct) =>
        db.Workspaces.AsNoTracking().OrderBy(w => w.CreatedAt)
            .ToPagedResponseAsync(paging, ExecutionMapping.ToDto, ct);

    /// <summary>Returns the default workspace, creating it on first run.</summary>
    [HttpGet("default")]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkspaceDto>> GetDefault(CancellationToken ct) =>
        Ok((await DatabaseInitializer.EnsureDefaultWorkspaceAsync(db, ct)).ToDto());

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkspaceDto>> Get(Guid id, CancellationToken ct)
    {
        var workspace = await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        return workspace is null ? NotFoundProblem("Workspace", id) : Ok(workspace.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkspaceDto>> Create([FromBody] CreateWorkspaceRequest request, CancellationToken ct)
    {
        var workspace = new Workspace(request.Name);
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = workspace.Id }, workspace.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkspaceDto>> Update(Guid id, [FromBody] UpdateWorkspaceRequest request,
        CancellationToken ct)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workspace is null) return NotFoundProblem("Workspace", id);

        workspace.Rename(request.Name);
        await db.SaveChangesAsync(ct);
        return Ok(workspace.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workspace is null) return NotFoundProblem("Workspace", id);

        db.Workspaces.Remove(workspace);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
