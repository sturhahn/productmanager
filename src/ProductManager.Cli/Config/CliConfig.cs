namespace ProductManager.Cli.Config;

/// <summary>
/// Persisted CLI configuration: which API to talk to and the bearer token from the last
/// successful <c>pm auth login</c>. Lives at <c>~/.productmanager/config.json</c>.
/// </summary>
public sealed record CliConfig
{
    public string BaseUrl { get; init; } = "http://localhost:5072";
    public string? AccessToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>True once we hold a token that has not passed its expiry (with a small safety margin).</summary>
    public bool HasUsableToken =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        (ExpiresAt is null || ExpiresAt.Value > DateTimeOffset.UtcNow.AddSeconds(30));
}
