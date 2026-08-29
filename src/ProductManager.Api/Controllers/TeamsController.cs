using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Common;
using ProductManager.Domain.Execution;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Capacity budget units. No per-person allocation and no skills model — a team's capacity per
/// period is a feasibility constraint checked against roadmap effort demand.
/// </summary>
[Route("api/teams")]
public sealed class TeamsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<TeamDto>), StatusCodes.Status200OK)]
    public Task<PagedResponse<TeamDto>> List([FromQuery] Guid? workspaceId, [FromQuery] PageQuery paging,
        CancellationToken ct) =>
        db.Teams.AsNoTracking()
            .Where(t => workspaceId == null || t.WorkspaceId == workspaceId)
            .OrderBy(t => t.Name)
            .ToPagedResponseAsync(paging, ExecutionMapping.ToDto, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TeamDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TeamDto>> Get(Guid id, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return team is null ? NotFoundProblem("Team", id) : Ok(team.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(TeamDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TeamDto>> Create([FromBody] CreateTeamRequest request, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == request.WorkspaceId, ct))
            return NotFoundProblem("Workspace", request.WorkspaceId);

        var team = new Team(request.WorkspaceId, request.Name, request.SpecializationTag);
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = team.Id }, team.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TeamDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TeamDto>> Update(Guid id, [FromBody] UpdateTeamRequest request, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return NotFoundProblem("Team", id);

        team.Rename(request.Name);
        team.SpecializationTag = request.SpecializationTag;
        await db.SaveChangesAsync(ct);
        return Ok(team.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return NotFoundProblem("Team", id);

        db.Teams.Remove(team);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Upserts the team's capacity for one period.</summary>
    [HttpPut("{id:guid}/capacities")]
    [ProducesResponseType(typeof(TeamDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TeamDto>> SetCapacity(Guid id, [FromBody] SetTeamCapacityRequest request,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return NotFoundProblem("Team", id);

        team.SetCapacity(new Period(request.Year, request.Month), request.Points);
        await db.SaveChangesAsync(ct);
        return Ok(team.ToDto());
    }
}
