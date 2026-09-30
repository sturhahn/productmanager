using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductManager.Api.Contracts;

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

    /// <summary>409 a client can tell apart by its <c>type</c>: the request contradicts recorded history.</summary>
    protected ObjectResult CorrectionRequiredProblem(string detail) =>
        Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Correction required",
            type: ProblemTypes.CorrectionRequired);

    protected ObjectResult BadRequestProblem(string detail) =>
        Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");

    /// <summary>
    /// Resolves the recorded date of a ledger entry: the caller's <paramref name="recordedAt"/> when
    /// this write is an import or backfill of something we learned in the past, otherwise now.
    ///
    /// <para>
    /// Backdating <em>when we learned</em> something is what makes an imported history usable for
    /// "what did we know as of date D" — stamping every imported fact with the import's wall clock
    /// would make every backdated calibration see an empty ledger. It is deliberately one-directional:
    /// a recorded date in the future is refused, because a fact cannot have been known before it was
    /// written, and allowing it would let a fit read evidence that does not exist yet.
    /// </para>
    /// </summary>
    protected static bool TryResolveRecordedDate(
        DateTimeOffset? recordedAt, out DateTimeOffset recordedDate, out string? error)
    {
        var now = DateTimeOffset.UtcNow;
        recordedDate = recordedAt ?? now;
        error = null;
        if (recordedAt is null || recordedDate <= now) return true;

        recordedDate = now;
        error = $"recordedAt {recordedAt:O} is in the future. The recorded date is when the fact " +
                "entered the ledger; it can be backdated for an import, never post-dated.";
        return false;
    }
}
