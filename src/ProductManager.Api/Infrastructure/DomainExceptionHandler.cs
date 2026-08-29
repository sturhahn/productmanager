using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ProductManager.Api.Infrastructure;

/// <summary>
/// Translates domain-invariant violations into RFC 7807 ProblemDetails responses.
/// The domain guards itself with exceptions; the API is responsible for the HTTP shape.
/// </summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = Classify(exception);
        if (status is null)
            return false;

        if (status >= 500)
            logger.LogError(exception, "Unhandled server error");
        else
            logger.LogInformation("Request rejected: {Message}", exception.Message);

        httpContext.Response.StatusCode = status.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
                Type = $"https://httpstatuses.io/{status}"
            }
        });
    }

    private static (int? Status, string Title) Classify(Exception exception) => exception switch
    {
        ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent modification"),
        DbUpdateException => (StatusCodes.Status409Conflict, "Conflicting write"),
        InvalidOperationException => (StatusCodes.Status409Conflict, "Operation not allowed in this state"),
        _ => (null, string.Empty)
    };
}
