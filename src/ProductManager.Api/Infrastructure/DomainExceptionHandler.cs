using System.Data.Common;
using System.Net.Sockets;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductManager.Domain.Common;

namespace ProductManager.Api.Infrastructure;

/// <summary>
/// Translates exceptions into RFC 7807 ProblemDetails responses.
/// <para>
/// The classification answers one question: <em>whose problem is it?</em> A domain rule the caller
/// broke is a 4xx and retrying verbatim will not help; a database that is down or unreachable is a
/// 503 and retrying almost certainly will. That distinction cannot be made on
/// <see cref="InvalidOperationException"/> alone — the domain used to raise it for state-rule
/// violations while ADO.NET providers raise it for broken connections, so every outage looked to
/// clients like a client-state conflict. The domain now raises <see cref="DomainConflictException"/>,
/// and the infrastructure cases below are recognised by exception type and provider origin (never by
/// matching message text).
/// </para>
/// </summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Seconds advertised in <c>Retry-After</c> when the datastore is unavailable.</summary>
    internal const int RetryAfterSeconds = 5;

    private const string RetryHint =
        "The service could not reach its datastore. This is usually transient — retry the request in a few seconds.";

    private const string ServerErrorDetail =
        "The server failed to process the request. The failure has been logged with this traceId.";

    /// <summary>
    /// ADO.NET providers that throw framework exception types (notably
    /// <see cref="InvalidOperationException"/> for a connection that is closed, broken, or exhausted
    /// from the pool). An exception thrown from inside one of these assemblies is an infrastructure
    /// fault, whatever its CLR type.
    /// </summary>
    private static readonly string[] DataProviderAssemblies =
        ["Npgsql", "Microsoft.Data.Sqlite", "System.Data.Common"];

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = Classify(exception);

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled server error ({Status})", status);
        else
            logger.LogInformation("Request rejected ({Status}): {Message}", status, exception.Message);

        httpContext.Response.StatusCode = status;
        if (status == StatusCodes.Status503ServiceUnavailable)
            httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = detail,
                Type = $"https://httpstatuses.io/{status}"
            }
        });
    }

    /// <summary>
    /// Maps an exception to (status, title, detail). Detail is the exception message only where the
    /// message is domain-authored and safe to show; infrastructure failures get a fixed hint instead
    /// so connection strings and schema internals never reach a client.
    /// </summary>
    internal static (int Status, string Title, string Detail) Classify(Exception exception) => exception switch
    {
        // The caller broke a state rule the domain guards.
        DomainConflictException => (StatusCodes.Status409Conflict,
            "Operation not allowed in this state", exception.Message),

        // Malformed or out-of-range input. ArgumentNullException and ArgumentOutOfRangeException derive
        // from ArgumentException and are equally the caller's problem.
        ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request", exception.Message),

        KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found", exception.Message),

        // A write that failed. Ordering matters: an unreachable database also surfaces here, wrapped.
        DbUpdateException dbUpdate => ClassifyWriteFailure(dbUpdate),

        _ when IsInfrastructureFailure(exception) => (StatusCodes.Status503ServiceUnavailable,
            "Service temporarily unavailable", RetryHint),

        _ => (StatusCodes.Status500InternalServerError, "Unexpected error", ServerErrorDetail)
    };

    private static (int Status, string Title, string Detail) ClassifyWriteFailure(DbUpdateException exception)
    {
        if (IsInfrastructureFailure(exception))
            return (StatusCodes.Status503ServiceUnavailable, "Service temporarily unavailable", RetryHint);

        var title = SqlStateOf(exception) switch
        {
            PostgresErrorCodes.UniqueViolation => "Duplicate value",
            PostgresErrorCodes.ForeignKeyViolation => "Referenced record missing or still in use",
            _ when exception is DbUpdateConcurrencyException => "Concurrent modification",
            _ => "Conflicting write"
        };

        // The EF message ("An error occurred while saving the entity changes…") is provider-neutral;
        // the inner exception's text is not surfaced.
        return (StatusCodes.Status409Conflict, title, exception.Message);
    }

    /// <summary>
    /// True when the failure is the datastore's rather than the caller's: a connection that could not
    /// be opened or that broke mid-flight, a socket or timeout error, a provider-declared transient
    /// condition, or any exception thrown from inside an ADO.NET provider.
    /// </summary>
    private static bool IsInfrastructureFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                // A response from a reachable server (constraint violation, syntax error): the connection
                // is fine, so this is not an outage — unless the server itself flags it as retryable.
                case PostgresException postgres:
                    if (postgres.IsTransient) return true;
                    break;

                // Every other NpgsqlException is a connectivity failure: refused, broken, timed out,
                // pool exhausted, TLS negotiation failed.
                case NpgsqlException:
                case SocketException:
                case TimeoutException:
                    return true;

                case DbException { IsTransient: true }:
                    return true;

                // Any other provider error carries a server response (a constraint violation, a bad
                // statement): judged above, and not an outage. Must precede the origin check below —
                // it is thrown from the provider assembly too.
                case DbException:
                    break;

                default:
                    if (ThrownByDataProvider(current)) return true;
                    break;
            }
        }

        return false;
    }

    private static string? SqlStateOf(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is DbException { SqlState: { } sqlState })
                return sqlState;
        return null;
    }

    /// <summary>
    /// Whether the exception was raised from inside an ADO.NET provider. <c>TargetSite</c> names the
    /// throwing method; <c>Source</c> (set by the runtime from the throw site) is the fallback when the
    /// method handle is unavailable, e.g. after a trimmed or serialized stack.
    /// </summary>
    private static bool ThrownByDataProvider(Exception exception)
    {
        var origin = exception.TargetSite?.DeclaringType?.Assembly.GetName().Name ?? exception.Source;
        return origin is not null && DataProviderAssemblies.Contains(origin);
    }
}
