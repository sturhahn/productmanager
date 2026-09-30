using System.CommandLine;
using System.Net.Http.Headers;
using System.Text.Json;
using ProductManager.Cli.Client;
using ProductManager.Cli.Config;

namespace ProductManager.Cli.Commands;

/// <summary>Shared plumbing every command group uses: the global --base-url option, building an
/// authenticated API client from the persisted config, JSON output, and a uniform error handler.</summary>
internal static class CliInfrastructure
{
    /// <summary>Recursive so it can be given anywhere on the command line, e.g. "pm --base-url ... team list".</summary>
    public static readonly Option<string?> BaseUrlOption = new("--base-url")
    {
        Description = "Override the configured API base URL for this invocation only.",
        Recursive = true
    };

    private static readonly JsonSerializerOptions PrettyJson = new(ProductManagerApiClient.JsonOptions)
    {
        WriteIndented = true
    };

    /// <summary>Builds an authenticated client from the persisted config. Throws <see cref="NotAuthenticatedException"/>
    /// if there is no usable token — every command except 'auth login' needs one.</summary>
    public static ProductManagerApiClient BuildAuthenticatedClient(ParseResult parseResult)
    {
        var config = ConfigStore.Load();
        if (!config.HasUsableToken)
            throw new NotAuthenticatedException();

        return BuildClient(ResolveBaseUrl(parseResult, config), config.AccessToken);
    }

    /// <summary>Builds an unauthenticated client for 'auth login' itself.</summary>
    public static (ProductManagerApiClient Client, string BaseUrl) BuildAnonymousClient(ParseResult parseResult)
    {
        var config = ConfigStore.Load();
        var baseUrl = ResolveBaseUrl(parseResult, config);
        return (BuildClient(baseUrl, accessToken: null), baseUrl);
    }

    private static string ResolveBaseUrl(ParseResult parseResult, CliConfig config)
    {
        var overrideUrl = parseResult.GetValue(BaseUrlOption);
        return string.IsNullOrWhiteSpace(overrideUrl) ? config.BaseUrl : overrideUrl;
    }

    private static ProductManagerApiClient BuildClient(string baseUrl, string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("No API base URL configured. Run 'pm auth login --password <password> --base-url <url>'.");

        Uri baseUri;
        try
        {
            baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        }
        catch (UriFormatException ex)
        {
            throw new ArgumentException($"'{baseUrl}' is not a valid URL.", ex);
        }

        var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(accessToken))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return new ProductManagerApiClient(http);
    }

    public static void WriteJson<T>(T value) => Console.WriteLine(ToJson(value));

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, PrettyJson);

    /// <summary>Every leaf command's action body runs through here so connection/auth/API failures
    /// produce one clear line instead of a raw stack trace.</summary>
    public static async Task<int> RunAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        try
        {
            await action(ct);
            return 0;
        }
        catch (ApiConnectionException ex)
        {
            await Console.Error.WriteLineAsync($"Connection failed: {ex.Message}");
            await Console.Error.WriteLineAsync(
                "Is the API running and reachable at that address? Start it with " +
                "'dotnet run --project src/ProductManager.Api', or point the CLI elsewhere with --base-url.");
            return 2;
        }
        catch (NotAuthenticatedException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 3;
        }
        catch (ApiRequestException ex)
        {
            await Console.Error.WriteLineAsync($"API rejected the request ({ex.Message})");
            return 4;
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync($"Invalid input: {ex.Message}");
            return 1;
        }
        catch (ExitCodeException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return ex.ExitCode;
        }
    }
}

/// <summary>A command that finished but must not report success, with its own exit code (e.g. 5:
/// <c>pm seed</c> completed with per-record exceptions).</summary>
internal sealed class ExitCodeException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
