using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Common;
using ProductManager.Domain.Execution;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Units of execution: dates, per-team per-period effort demand, dependencies, and the seam
/// (a project delivers features).
/// </summary>
[Route("api/projects")]
public sealed class ProjectsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProjectDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<ProjectDto>> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? initiativeId,
        [FromQuery] PageQuery paging,
        CancellationToken ct)
    {
        var query = db.Projects.AsNoTracking().AsQueryable();

        if (initiativeId is not null)
            query = query.Where(p => p.InitiativeId == initiativeId);

        if (workspaceId is not null)
            query = query.Where(p => db.Initiatives
                .Any(i => i.Id == p.InitiativeId && i.WorkspaceId == workspaceId));

        return query.OrderBy(p => p.Name).ToPagedResponseAsync(paging, ExecutionMapping.ToDto, ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> Get(Guid id, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return project is null ? NotFoundProblem("Project", id) : Ok(project.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProjectDto>> Create([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        if (!await db.Initiatives.AnyAsync(i => i.Id == request.InitiativeId, ct))
            return NotFoundProblem("Initiative", request.InitiativeId);

        var project = new Project(request.InitiativeId, request.Name, request.StartDate, request.EndDate);
        project.Description = request.Description;
        if (request.Status is not null) project.SetStatus(request.Status.Value);

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = project.Id }, project.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> Update(Guid id, [FromBody] UpdateProjectRequest request,
        CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFoundProblem("Project", id);

        project.Rename(request.Name);
        project.Description = request.Description;
        project.SetDates(request.StartDate, request.EndDate);
        if (request.Status is not null) project.SetStatus(request.Status.Value);

        await db.SaveChangesAsync(ct);
        return Ok(project.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFoundProblem("Project", id);

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- Dependencies ----------

    /// <summary>Declares that this project cannot start before another project finishes.</summary>
    [HttpPost("{id:guid}/dependencies")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectDto>> AddDependency(Guid id, [FromBody] AddProjectDependencyRequest request,
        CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFoundProblem("Project", id);
        if (!await db.Projects.AnyAsync(p => p.Id == request.DependsOnProjectId, ct))
            return NotFoundProblem("Project", request.DependsOnProjectId);

        db.MutateChildren(() => project.Dependencies, () => project.AddDependency(request.DependsOnProjectId));
        await db.SaveChangesAsync(ct);
        return Ok(project.ToDto());
    }

    [HttpDelete("{id:guid}/dependencies/{dependsOnProjectId:guid}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> RemoveDependency(Guid id, Guid dependsOnProjectId, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFoundProblem("Project", id);

        project.RemoveDependency(dependsOnProjectId);
        await db.SaveChangesAsync(ct);
        return Ok(project.ToDto());
    }

    // ---------- Effort demand ----------

    /// <summary>Upserts the effort this project demands from a team in a period. Zero points clears it.</summary>
    [HttpPut("{id:guid}/effort-demands")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> SetEffortDemand(Guid id, [FromBody] SetEffortDemandRequest request,
        CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFoundProblem("Project", id);
        if (!await db.Teams.AnyAsync(t => t.Id == request.TeamId, ct))
            return NotFoundProblem("Team", request.TeamId);

        project.SetEffortDemand(request.TeamId, new Period(request.Year, request.Month), request.Points);
        await db.SaveChangesAsync(ct);
        return Ok(project.ToDto());
    }

    // ---------- The seam: delivered features ----------

    /// <summary>Links a feature this project delivers — the one join between execution and market sides.</summary>
    [HttpPost("{id:guid}/features")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> LinkFeature(Guid id, [FromBody] LinkFeatureRequest request,
        CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFoundProblem("Project", id);
        if (!await db.Features.AnyAsync(f => f.Id == request.FeatureId, ct))
            return NotFoundProblem("Feature", request.FeatureId);

        db.MutateChildren(() => project.DeliveredFeatures, () => project.LinkFeature(request.FeatureId));
        await db.SaveChangesAsync(ct);
        return Ok(project.ToDto());
    }

    [HttpDelete("{id:guid}/features/{featureId:guid}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> UnlinkFeature(Guid id, Guid featureId, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFoundProblem("Project", id);

        project.UnlinkFeature(featureId);
        await db.SaveChangesAsync(ct);
        return Ok(project.ToDto());
    }
}
