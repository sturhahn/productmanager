using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Execution;
using ProductManager.Domain.Services;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Branchable, versioned plans. The live roadmap is a living document; a scenario is a branch
/// (deep copy of the plan links) and adopting a scenario is a merge back into the live roadmap.
/// </summary>
[Route("api/roadmaps")]
public sealed class RoadmapsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<RoadmapDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<RoadmapDto>> List(
        [FromQuery] Guid? workspaceId,
        [FromQuery] bool? isLive,
        [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Roadmaps.AsNoTracking()
            .Where(r => (workspaceId == null || r.WorkspaceId == workspaceId)
                        && (isLive == null || r.IsLive == isLive))
            .OrderByDescending(r => r.IsLive).ThenBy(r => r.CreatedAt)
            .ToPagedResponseAsync(paging, RoadmapMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoadmapDto>> Get(Guid id, CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return roadmap is null ? NotFoundProblem("Roadmap", id) : Ok(roadmap.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RoadmapDto>> Create([FromBody] CreateRoadmapRequest request, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return NotFoundProblem("Workspace", request.WorkspaceId);

        var roadmap = new Roadmap(request.WorkspaceId, request.Name) { VersionName = request.VersionName };
        db.Roadmaps.Add(roadmap);

        if (request.IsLive)
        {
            await RetireOtherLiveRoadmapsAsync(request.WorkspaceId, roadmap.Id, ct);
            roadmap.MakeLive();
        }

        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = roadmap.Id }, roadmap.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoadmapDto>> Update(Guid id, [FromBody] UpdateRoadmapRequest request,
        CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);

        roadmap.Rename(request.Name);
        roadmap.VersionName = request.VersionName;
        await db.SaveChangesAsync(ct);
        return Ok(roadmap.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);
        if (roadmap.IsLive)
            return ConflictProblem("The live roadmap cannot be deleted; make another roadmap live first.");

        db.Roadmaps.Remove(roadmap);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Makes this roadmap the workspace's live plan, retiring whichever was live before.</summary>
    [HttpPost("{id:guid}/make-live")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoadmapDto>> MakeLive(Guid id, CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);

        await RetireOtherLiveRoadmapsAsync(roadmap.WorkspaceId, roadmap.Id, ct);
        roadmap.MakeLive();
        await db.SaveChangesAsync(ct);
        return Ok(roadmap.ToDto());
    }

    // ---------- Plan links (timeline items) ----------

    /// <summary>Plans (or re-plans) a project on this roadmap.</summary>
    [HttpPost("{id:guid}/items")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoadmapDto>> PlanProject(Guid id, [FromBody] PlanProjectRequest request,
        CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);
        if (!await db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct))
            return NotFoundProblem("Project", request.ProjectId);

        db.MutateChildren(() => roadmap.Items,
            () => roadmap.PlanProject(request.ProjectId, request.PlannedStart, request.PlannedEnd, request.SortOrder));
        await db.SaveChangesAsync(ct);
        return Ok(roadmap.ToDto());
    }

    /// <summary>Moves a planned project on the timeline — the drag-and-drop reschedule.</summary>
    [HttpPut("{id:guid}/items/{projectId:guid}")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoadmapDto>> RescheduleItem(Guid id, Guid projectId,
        [FromBody] RescheduleItemRequest request, CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);

        var item = roadmap.Items.FirstOrDefault(i => i.ProjectId == projectId);
        if (item is null) return NotFoundProblem("Roadmap item for project", projectId);

        item.Reschedule(request.PlannedStart, request.PlannedEnd);
        if (request.SortOrder is not null) item.Reorder(request.SortOrder.Value);

        await db.SaveChangesAsync(ct);
        return Ok(roadmap.ToDto());
    }

    /// <summary>Removes a project from this roadmap.</summary>
    [HttpDelete("{id:guid}/items/{projectId:guid}")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoadmapDto>> UnplanProject(Guid id, Guid projectId, CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);

        roadmap.UnplanProject(projectId);
        await db.SaveChangesAsync(ct);
        return Ok(roadmap.ToDto());
    }

    /// <summary>
    /// Rewrites the display order: each project's sort order becomes its index in the posted list.
    /// The list must name exactly the projects planned on this roadmap.
    /// </summary>
    [HttpPut("{id:guid}/order")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoadmapDto>> Reorder(Guid id, [FromBody] ReorderRoadmapRequest request,
        CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);

        var planned = roadmap.Items.ToDictionary(i => i.ProjectId);
        var requested = request.ProjectIds.Distinct().ToList();

        if (requested.Count != request.ProjectIds.Count)
            return Problem(detail: "The order must not repeat a project.",
                statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");

        var unknown = requested.Where(p => !planned.ContainsKey(p)).ToList();
        if (unknown.Count > 0)
            return Problem(detail: $"These projects are not planned on this roadmap: {string.Join(", ", unknown)}.",
                statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");

        if (requested.Count != planned.Count)
            return Problem(
                detail: $"The order must list all {planned.Count} planned projects; {requested.Count} were given.",
                statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");

        for (var index = 0; index < requested.Count; index++)
            planned[requested[index]].Reorder(index);

        await db.SaveChangesAsync(ct);
        return Ok(roadmap.ToDto());
    }

    // ---------- Branch / adopt ----------

    /// <summary>Branches this roadmap into a scenario: a new, non-live roadmap with a deep copy of the plan.</summary>
    [HttpPost("{id:guid}/branch")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RoadmapDto>> Branch(Guid id, [FromBody] BranchRoadmapRequest request,
        CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);

        var branch = roadmap.Branch(request.Name);
        db.Roadmaps.Add(branch);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = branch.Id }, branch.ToDto());
    }

    /// <summary>Adopts a scenario branch into this (live) roadmap — the merge back to main.</summary>
    [HttpPost("{id:guid}/adopt")]
    [ProducesResponseType(typeof(RoadmapDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoadmapDto>> Adopt(Guid id, [FromBody] AdoptBranchRequest request,
        CancellationToken ct)
    {
        var live = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (live is null) return NotFoundProblem("Roadmap", id);

        var branch = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == request.BranchRoadmapId, ct);
        if (branch is null) return NotFoundProblem("Roadmap", request.BranchRoadmapId);

        db.MutateChildren(() => live.Items, () => live.AdoptBranch(branch));
        await db.SaveChangesAsync(ct);
        return Ok(live.ToDto());
    }

    // ---------- Feasibility ----------

    /// <summary>
    /// Team-capacity overallocations and dependency-ordering violations for this roadmap.
    /// Capacity is a feasibility constraint: demand above capacity in a period is flagged, never
    /// silently accepted.
    /// </summary>
    [HttpGet("{id:guid}/feasibility")]
    [ProducesResponseType(typeof(RoadmapFeasibilityDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoadmapFeasibilityDto>> Feasibility(Guid id, CancellationToken ct)
    {
        var roadmap = await db.Roadmaps.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFoundProblem("Roadmap", id);

        var plannedProjectIds = roadmap.Items.Select(i => i.ProjectId).ToList();

        var projects = await db.Projects.AsNoTracking()
            .Where(p => plannedProjectIds.Contains(p.Id))
            .ToListAsync(ct);

        var teams = await db.Teams.AsNoTracking()
            .Where(t => t.WorkspaceId == roadmap.WorkspaceId)
            .ToListAsync(ct);

        var teamNames = teams.ToDictionary(t => t.Id, t => t.Name);

        var capacityWarnings = CapacityFeasibilityService.ComputeOverallocations(teams, projects)
            .Select(w => w.ToDto(teamNames.GetValueOrDefault(w.TeamId)))
            .ToList();

        var sequencingViolations = RoadmapSequencingService.DetectViolations(roadmap, projects)
            .Select(RoadmapMapping.ToDto)
            .ToList();

        return Ok(new RoadmapFeasibilityDto(
            roadmap.Id,
            capacityWarnings.Count == 0 && sequencingViolations.Count == 0,
            capacityWarnings,
            sequencingViolations));
    }

    private async Task RetireOtherLiveRoadmapsAsync(Guid workspaceId, Guid keepId, CancellationToken ct)
    {
        var currentlyLive = await db.Roadmaps
            .Where(r => r.WorkspaceId == workspaceId && r.IsLive && r.Id != keepId)
            .ToListAsync(ct);
        foreach (var other in currentlyLive)
            other.RetireFromLive();
    }
}
