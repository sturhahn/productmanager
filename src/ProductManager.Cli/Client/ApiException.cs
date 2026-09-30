using System.Net;

namespace ProductManager.Cli.Client;

/// <summary>Base type for everything that can go wrong talking to the API.</summary>
public abstract class ApiClientException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The API could not be reached at all — connection refused, DNS failure, timeout. Distinct from
/// <see cref="ApiRequestException"/> so callers (and the top-level error handler) can give the
/// user an actionable "is the API running?" message instead of an HTTP-status message.
/// </summary>
public sealed class ApiConnectionException(string baseUrl, Exception inner)
    : ApiClientException($"Could not reach the ProductManager API at {baseUrl}.", inner)
{
    public string BaseUrl { get; } = baseUrl;
}

/// <summary>The API responded, but with a non-success status. Carries the RFC 7807 details when present.</summary>
public sealed class ApiRequestException(HttpStatusCode statusCode, string? title, string? detail, string? type = null)
    : ApiClientException(BuildMessage(statusCode, title, detail))
{
    /// <summary>The ProblemDetails <c>type</c> the API sends when a restated decision or sourced fact
    /// differs from what is recorded: nothing was changed, and a retry will not help.</summary>
    public const string CorrectionRequiredType = "urn:productmanager:problem:correction-required";

    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? Title { get; } = title;
    public string? Detail { get; } = detail;
    public string? Type { get; } = type;

    public bool IsCorrectionRequired => StatusCode == HttpStatusCode.Conflict && Type == CorrectionRequiredType;

    private static string BuildMessage(HttpStatusCode statusCode, string? title, string? detail)
    {
        var head = $"{(int)statusCode} {statusCode}" + (title is null ? "" : $" — {title}");
        return detail is null ? head : $"{head}: {detail}";
    }
}

/// <summary>No usable bearer token is configured — the user needs to run <c>pm auth login</c> first.</summary>
public sealed class NotAuthenticatedException()
    : ApiClientException("Not signed in (or the token expired). Run 'pm auth login --password <password>' first.");

internal sealed record ProblemDetailsDto(string? Title, string? Detail, int? Status, string? Type);
