using Microsoft.EntityFrameworkCore;
using ProductManager.Api.Persistence;
using ProductManager.Domain.Execution;

namespace ProductManager.Api.Infrastructure;

/// <summary>
/// Brings the schema up to date and guarantees the workspace root aggregate exists.
/// Phase 1 is single-user and single-workspace by default: the first run creates one.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    /// <summary>Name of the workspace created on first run.</summary>
    public const string DefaultWorkspaceName = "Default Workspace";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Database:InitializeOnStartup", true))
            return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            if (db.Database.IsNpgsql())
                await db.Database.MigrateAsync(cancellationToken);
            else
                await db.Database.EnsureCreatedAsync(cancellationToken);

            await EnsureDefaultWorkspaceAsync(db, cancellationToken);
        }
        catch (Exception ex) when (configuration.GetValue("Database:ContinueOnInitializationFailure", true))
        {
            // A missing database must not stop the API from starting (swagger, /health and the
            // OpenAPI document stay reachable); the failure is loud in the logs instead.
            logger.LogError(ex, "Database initialization failed; the API is starting without a ready database.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Creates the default workspace if the database has none. Idempotent.</summary>
    public static async Task<Workspace> EnsureDefaultWorkspaceAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.Workspaces.OrderBy(w => w.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return existing;

        var workspace = new Workspace(DefaultWorkspaceName);
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);
        return workspace;
    }
}
