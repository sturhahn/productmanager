using System.Net;
using System.Text.Json;

namespace ProductManager.Api.Tests;

/// <summary>
/// The OpenAPI document is the contract the CLI, the web app and (later) the MCP server are
/// written against, so it is checked in. This test is the generator: running the test suite
/// refreshes <c>openapi/productmanager.json</c> from the live route and fails if the API cannot
/// produce a document at all.
/// </summary>
public sealed class OpenApiDocumentTests : IAsyncLifetime
{
    /// <summary>Repo-relative path of the checked-in contract.</summary>
    public const string ContractPath = "openapi/productmanager.json";

    private const string DocumentRoute = "/swagger/v1/swagger.json";

    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_document_is_served_anonymously_and_written_to_the_repo()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(DocumentRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.StartsWith("3.", root.GetProperty("openapi").GetString());
        Assert.Equal("ProductManager API", root.GetProperty("info").GetProperty("title").GetString());

        var paths = root.GetProperty("paths");
        foreach (var expected in new[]
                 {
                     "/api/auth/token",
                     "/api/workspaces",
                     "/api/teams",
                     "/api/initiatives",
                     "/api/projects",
                     "/api/features",
                     "/api/roadmaps",
                     "/api/roadmaps/{id}/branch",
                     "/api/roadmaps/{id}/adopt",
                     "/api/roadmaps/{id}/order",
                     "/api/roadmaps/{id}/feasibility",
                     "/api/markets",
                     "/api/client-segments",
                     "/api/business-processes",
                     "/api/products",
                     "/api/coverages"
                 })
        {
            Assert.True(paths.TryGetProperty(expected, out _), $"OpenAPI document is missing {expected}");
        }

        Assert.True(root.GetProperty("components").GetProperty("securitySchemes")
            .TryGetProperty("bearerAuth", out _));

        WriteContractFile(json);
    }

    private static void WriteContractFile(string json)
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null) return; // running outside the repo (e.g. a packaged test run)

        var target = Path.Combine(repoRoot, ContractPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var parsed = JsonDocument.Parse(json);
        var pretty = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true }) + "\n";

        // Only rewrite on a real change, so an unchanged contract stays out of the diff.
        if (File.Exists(target) && File.ReadAllText(target) == pretty) return;
        File.WriteAllText(target, pretty);
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProductManager.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }
}
