using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProductManager.Domain.Execution;
using ProductManager.Domain.Facts;
using ProductManager.Domain.Imports;
using ProductManager.Domain.Market;
using ProductManager.Domain.Seam;
using ProductManager.Domain.Simulation;

namespace ProductManager.Api.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Execution side
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Roadmap> Roadmaps => Set<Roadmap>();
    public DbSet<RoadmapItem> RoadmapItems => Set<RoadmapItem>();
    public DbSet<Initiative> Initiatives => Set<Initiative>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Team> Teams => Set<Team>();

    // The seam
    public DbSet<Feature> Features => Set<Feature>();

    // Market side — structure
    public DbSet<Market> Markets => Set<Market>();
    public DbSet<ClientSegment> ClientSegments => Set<ClientSegment>();
    public DbSet<Firm> Firms => Set<Firm>();
    public DbSet<Competitor> Competitors => Set<Competitor>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<FirmProductRelationship> FirmProductRelationships => Set<FirmProductRelationship>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<Channel> Channels => Set<Channel>();

    // Market side — demand & supply
    public DbSet<BusinessProcess> BusinessProcesses => Set<BusinessProcess>();
    public DbSet<Approach> Approaches => Set<Approach>();
    public DbSet<Need> Needs => Set<Need>();
    public DbSet<Coverage> Coverages => Set<Coverage>();

    // Market side — dynamics
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<MarketEvent> MarketEvents => Set<MarketEvent>();
    public DbSet<Covariate> Covariates => Set<Covariate>();

    // Simulation & calibration (schema stubs in Phase 1)
    public DbSet<AssumptionSet> AssumptionSets => Set<AssumptionSet>();
    public DbSet<Parameter> Parameters => Set<Parameter>();
    public DbSet<CalibrationRun> CalibrationRuns => Set<CalibrationRun>();
    public DbSet<SimulationRun> SimulationRuns => Set<SimulationRun>();

    // Facts ledger (bi-temporal, append-only)
    public DbSet<FactEvent> FactEvents => Set<FactEvent>();

    // Import provenance: which source record became which row
    public DbSet<SourceBinding> SourceBindings => Set<SourceBinding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // PostgreSQL maps List<Guid> natively to uuid[]. Non-Npgsql providers (the SQLite provider
        // used by the integration tests) have no array mapping, so those properties fall back to a
        // delimited-string conversion. The production model is unchanged.
        if (!Database.IsNpgsql())
        {
            var guidListConverter = new ValueConverter<List<Guid>, string>(
                v => string.Join(',', v),
                v => v.Length == 0
                    ? new List<Guid>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList());

            var guidListComparer = new ValueComparer<List<Guid>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                v => v.Aggregate(0, (acc, g) => HashCode.Combine(acc, g.GetHashCode())),
                v => v.ToList());

            // SQLite has no native DateTimeOffset, and cannot ORDER BY one. Storing UTC ticks keeps
            // instants ordered correctly.
            var utcTicksConverter = new ValueConverter<DateTimeOffset, long>(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));

            var nullableUtcTicksConverter = new ValueConverter<DateTimeOffset?, long?>(
                v => v == null ? null : v.Value.UtcTicks,
                v => v == null ? null : new DateTimeOffset(v.Value, TimeSpan.Zero));

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(utcTicksConverter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableUtcTicksConverter);
                }
                else if (property.ClrType == typeof(List<Guid>))
                {
                    property.SetValueConverter(guidListConverter);
                    property.SetValueComparer(guidListComparer);
                    property.SetColumnType(null);
                }
                else if (string.Equals(property.GetColumnType(), "jsonb", StringComparison.OrdinalIgnoreCase))
                {
                    property.SetColumnType(null);
                }
            }
        }
    }
}
