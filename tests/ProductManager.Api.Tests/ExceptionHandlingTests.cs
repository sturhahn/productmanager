using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Common;

namespace ProductManager.Api.Tests;

/// <summary>
/// The handler in isolation: every exception the API can meet, and the status it must produce.
/// The load-bearing case is the pair at the bottom — a broken database connection surfaces from
/// Npgsql as <see cref="InvalidOperationException"/>, and must not be reported to clients as the
/// 409 that a genuine state conflict earns.
/// </summary>
public class ExceptionClassificationTests
{
    [Fact]
    public async Task Domain_conflict_is_a_409_and_keeps_the_domain_message()
    {
        var result = await HandleAsync(new DomainConflictException("Opportunity is already decided."));

        Assert.Equal(StatusCodes.Status409Conflict, result.Status);
        Assert.Equal("Opportunity is already decided.", result.Body.Detail);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task Argument_exception_is_a_400()
    {
        var result = await HandleAsync(new ArgumentException("Planned end cannot precede planned start."));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
        Assert.Equal("Planned end cannot precede planned start.", result.Body.Detail);
    }

    [Fact]
    public async Task Missing_key_is_a_404()
    {
        var result = await HandleAsync(new KeyNotFoundException("No such roadmap."));

        Assert.Equal(StatusCodes.Status404NotFound, result.Status);
    }

    [Fact]
    public async Task A_unique_violation_on_write_is_a_409()
    {
        var violation = new PostgresException("duplicate key value violates unique constraint",
            "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        var result = await HandleAsync(new DbUpdateException("Save failed.", violation));

        Assert.Equal(StatusCodes.Status409Conflict, result.Status);
        Assert.Equal("Duplicate value", result.Body.Title);
    }

    [Fact]
    public async Task A_foreign_key_violation_on_write_is_a_409()
    {
        var violation = new PostgresException("violates foreign key constraint",
            "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation);

        var result = await HandleAsync(new DbUpdateException("Save failed.", violation));

        Assert.Equal(StatusCodes.Status409Conflict, result.Status);
        Assert.Equal("Referenced record missing or still in use", result.Body.Title);
    }

    [Fact]
    public async Task A_concurrency_conflict_is_a_409()
    {
        var result = await HandleAsync(new DbUpdateConcurrencyException("Row was modified."));

        Assert.Equal(StatusCodes.Status409Conflict, result.Status);
        Assert.Equal("Concurrent modification", result.Body.Title);
    }

    [Fact]
    public async Task An_unreachable_database_is_a_503_with_a_retry_hint()
    {
        var result = await HandleAsync(new NpgsqlException("Failed to connect to 127.0.0.1:5433"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.Status);
        Assert.Equal(DomainExceptionHandler.RetryAfterSeconds.ToString(), result.RetryAfter);
        Assert.Contains("retry", result.Body.Detail!, StringComparison.OrdinalIgnoreCase);
        // The provider's own message (host, port, connection string fragments) is not echoed back.
        Assert.DoesNotContain("127.0.0.1", result.Body.Detail!);
    }

    [Fact]
    public async Task A_write_that_fails_because_the_database_is_gone_is_a_503_not_a_409()
    {
        var outage = new DbUpdateException("Save failed.", new NpgsqlException("Connection broken."));

        var result = await HandleAsync(outage);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.Status);
        Assert.Equal(DomainExceptionHandler.RetryAfterSeconds.ToString(), result.RetryAfter);
    }

    /// <summary>The regression this handler exists for: Npgsql reports broken connections as
    /// <see cref="InvalidOperationException"/>, which used to be mapped wholesale to 409.</summary>
    [Fact]
    public async Task A_provider_thrown_InvalidOperationException_is_a_503()
    {
        var result = await HandleAsync(ProviderConnectionFailure());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.Status);
        Assert.Equal(DomainExceptionHandler.RetryAfterSeconds.ToString(), result.RetryAfter);
    }

    /// <summary>…while an <see cref="InvalidOperationException"/> from our own code is a bug, not a
    /// client conflict and not an outage.</summary>
    [Fact]
    public async Task An_InvalidOperationException_from_our_own_code_is_a_500()
    {
        var bug = Assert.Throws<InvalidOperationException>(() => Enumerable.Empty<int>().First());

        var result = await HandleAsync(bug);

        Assert.Equal(StatusCodes.Status500InternalServerError, result.Status);
        Assert.Null(result.RetryAfter);
        // Internal failure text stays in the log, not in the response body.
        Assert.DoesNotContain("sequence", result.Body.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A genuine Npgsql-thrown <see cref="InvalidOperationException"/>: a command executed against a
    /// connection that is not usable. Thrown (not merely constructed) so the throw site — the thing
    /// the handler classifies on — is real.
    /// </summary>
    private static InvalidOperationException ProviderConnectionFailure()
    {
        using var command = new NpgsqlCommand("select 1");
        return Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
    }

    private static async Task<(int Status, ProblemBody Body, string? RetryAfter)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        await using var provider = services.BuildServiceProvider();

        var handler = ActivatorUtilities.CreateInstance<DomainExceptionHandler>(provider);
        var context = new DefaultHttpContext { RequestServices = provider };
        var body = new MemoryStream();
        context.Response.Body = body;

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemBody>(body, ApiFactory.Json);
        return (context.Response.StatusCode, problem!,
            context.Response.Headers.RetryAfter.ToString() is { Length: > 0 } retry ? retry : null);
    }

    private sealed record ProblemBody(string? Title, string? Detail, int? Status);
}

/// <summary>The same distinction proven through the real HTTP pipeline.</summary>
public class ExceptionHandlingEndToEndTests : ApiTestBase
{
    [Fact]
    public async Task A_domain_state_rule_violation_returns_409()
    {
        var workspace = await DefaultWorkspaceAsync();
        var draft = await CreateRoadmapAsync(workspace.Id, "Draft");
        var branch = await PostAsync<RoadmapDto>($"/api/roadmaps/{draft.Id}/branch",
            new BranchRoadmapRequest("Scenario"));

        // Roadmap.AdoptBranch guards this: only the live roadmap may adopt.
        var response = await Client.PostAsJsonAsync($"/api/roadmaps/{draft.Id}/adopt",
            new AdoptBranchRequest(branch.Id), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_database_outage_returns_503_rather_than_409()
    {
        await using var factory = new UnreachableDatabaseFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        // Issuing a token needs no database; reading a workspace does.
        var response = await client.GetAsync($"/api/workspaces/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(DomainExceptionHandler.RetryAfterSeconds,
            response.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    /// <summary>
    /// The API wired to a PostgreSQL endpoint that refuses connections — an outage, simulated without
    /// touching production code.
    /// </summary>
    private sealed class UnreachableDatabaseFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // "Testing" keeps Program from registering its own provider; the initializer is allowed to
            // fail so the host still starts, exactly as it would against a database that went away.
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Auth:Password"] = ApiFactory.Password,
                    ["Auth:SigningKey"] = "integration-test-signing-key-0123456789",
                    ["Auth:Issuer"] = "productmanager",
                    ["Auth:Audience"] = "productmanager",
                    ["Database:InitializeOnStartup"] = "false"
                }));

            builder.ConfigureServices(services => services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql("Host=127.0.0.1;Port=1;Database=productmanager;Username=none;" +
                                  "Password=none;Timeout=2;Command Timeout=2")));
        }

        public async Task<HttpClient> CreateAuthenticatedClientAsync()
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/token",
                new TokenRequest(ApiFactory.Password), ApiFactory.Json);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ApiFactory.Json);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
            return client;
        }
    }
}
