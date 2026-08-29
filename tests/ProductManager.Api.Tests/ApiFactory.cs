using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductManager.Api.Contracts;
using ProductManager.Api.Persistence;

namespace ProductManager.Api.Tests;

/// <summary>
/// Boots the real API pipeline (auth, validation, ProblemDetails, controllers) against a private
/// SQLite in-memory database. SQLite is used rather than the EF in-memory provider because the
/// domain leans on relational behaviour — owned collections, cascade deletes, check constraints.
/// The Postgres-only bits (uuid[] and jsonb column types) fall back to portable mappings in
/// <see cref="AppDbContext.OnModelCreating"/> when the provider is not Npgsql.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "test-password";
    private const string SigningKey = "integration-test-signing-key-0123456789";

    // Held open for the lifetime of the factory: closing it drops the in-memory database.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Password"] = Password,
            ["Auth:SigningKey"] = SigningKey,
            ["Auth:Issuer"] = "productmanager",
            ["Auth:Audience"] = "productmanager",
            ["Database:ContinueOnInitializationFailure"] = "false"
        }));

        builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection)));
    }

    /// <summary>A client already carrying a valid bearer token.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new TokenRequest(Password), Json);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
