using System.Text.Json;
using ProductManager.Cli.Client;
using ProductManager.Cli.Seed;

namespace ProductManager.Cli.Tests;

/// <summary>
/// Parses the real seed/phase1-seed.json through the same options the CLI uses, and cross-checks
/// its natural-key references (the ones SeedLoader resolves via NameIndex) all resolve — catching
/// schema drift or a typo'd reference without needing a live API.
/// </summary>
public class SeedFileTests
{
    private static SeedFile LoadSeedFile()
    {
        var path = FindRepoFile("seed/phase1-seed.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SeedFile>(json, ProductManagerApiClient.JsonOptions)
            ?? throw new InvalidOperationException("seed/phase1-seed.json deserialized to null.");
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' by walking up from {AppContext.BaseDirectory}.");
    }

    [Fact]
    public void The_shipped_seed_file_parses_and_matches_the_scale_described_in_the_PRD_task()
    {
        var seed = LoadSeedFile();

        Assert.NotNull(seed.Workspace);
        Assert.Equal(4, seed.Teams.Count);
        Assert.Equal(3, seed.Initiatives.Count);
        Assert.True(seed.Projects.Count >= 8);
        Assert.True(seed.Features.Count >= 12);
        Assert.NotNull(seed.Roadmap);
        Assert.NotNull(seed.Market);
        Assert.Equal(3, seed.Market!.Segments.Count);
        Assert.True(seed.Market.BusinessProcesses.Count >= 8);
    }

    [Fact]
    public void At_least_one_period_is_deliberately_overallocated()
    {
        var seed = LoadSeedFile();

        var capacity = seed.Teams
            .SelectMany(t => t.Capacities.Select(c => (Key: (t.Name, c.Year, c.Month), c.Points)))
            .ToDictionary(x => x.Key, x => x.Points);

        var demand = new Dictionary<(string, int, int), decimal>();
        foreach (var project in seed.Projects)
        foreach (var effort in project.EffortDemands)
        {
            var key = (effort.TeamName, effort.Year, effort.Month);
            demand[key] = demand.GetValueOrDefault(key) + effort.Points;
        }

        var overallocated = demand.Any(kv => capacity.TryGetValue(kv.Key, out var cap) && kv.Value > cap);
        Assert.True(overallocated, "Expected at least one team/period where demand exceeds capacity.");
    }

    [Fact]
    public void Every_project_dependency_reference_resolves_to_a_project_in_the_file()
    {
        var seed = LoadSeedFile();
        var projectNames = seed.Projects.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var project in seed.Projects)
        foreach (var dep in project.DependsOnProjectNames)
            Assert.Contains(dep, projectNames);
    }

    [Fact]
    public void Every_project_initiative_and_feature_reference_resolves()
    {
        var seed = LoadSeedFile();
        var initiativeNames = seed.Initiatives.Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var featureNames = seed.Features.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var project in seed.Projects)
        {
            Assert.Contains(project.InitiativeName, initiativeNames);
            foreach (var featureName in project.DeliversFeatureNames)
                Assert.Contains(featureName, featureNames);
        }
    }

    [Fact]
    public void Every_roadmap_item_references_a_project_in_the_file()
    {
        var seed = LoadSeedFile();
        var projectNames = seed.Projects.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(seed.Roadmap!.Items, item => Assert.Contains(item.ProjectName, projectNames));
    }

    [Fact]
    public void Coverage_rows_use_every_tier_in_the_vocabulary()
    {
        var seed = LoadSeedFile();
        var tiers = seed.Market!.Coverages.Select(c => c.Tier).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Gold", tiers);
        Assert.Contains("Silver", tiers);
        Assert.Contains("Bronze", tiers);
        Assert.Contains("NotPlaying", tiers);
        Assert.Contains(seed.Market.Coverages, c => c.IsUnknown);
    }
}
