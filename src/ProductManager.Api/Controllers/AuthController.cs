using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductManager.Api.Auth;
using ProductManager.Api.Contracts;

namespace ProductManager.Api.Controllers;

/// <summary>Single-user sign-in. Phase 1 has one shared password and one bearer token.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(TokenIssuer tokenIssuer) : ControllerBase
{
    /// <summary>Exchanges the configured single-user password for a bearer token.</summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> IssueToken([FromBody] TokenRequest request)
    {
        if (!tokenIssuer.PasswordMatches(request.Password))
            return Problem(detail: "Invalid password.", statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed");

        var (token, expiresAt) = tokenIssuer.Issue();
        return Ok(new TokenResponse(token, "Bearer", expiresAt));
    }

    /// <summary>Echoes the authenticated subject — a cheap way for clients to check their token.</summary>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [Authorize]
    public ActionResult<object> Me() => Ok(new { subject = User.Identity?.Name ?? "pm", authenticated = true });
}
