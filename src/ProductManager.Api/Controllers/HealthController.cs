using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Persistence;

namespace ProductManager.Api.Controllers;

/// <summary>Liveness probe. Anonymous so clients can check the API before they hold a token.</summary>
[ApiController]
[Route("api/health")]
[AllowAnonymous]
public sealed class HealthController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> Get(CancellationToken ct)
    {
        var databaseReachable = await db.Database.CanConnectAsync(ct);
        return Ok(new { status = "ok", databaseReachable });
    }
}
