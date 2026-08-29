using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProductManager.Api.Controllers;

/// <summary>
/// Shared shape for every controller: authenticated, JSON, ProblemDetails on failure.
/// </summary>
[ApiController]
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult NotFoundProblem(string what, Guid id) =>
        Problem(
            detail: $"{what} '{id}' does not exist.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Not found");

    protected ObjectResult ConflictProblem(string detail) =>
        Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");
}
