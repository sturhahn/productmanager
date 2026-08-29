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
    private static SeedFile LoadSeedFile() => LoadSeedFile("seed/phase1-seed.json");

    private static SeedFile LoadPhase2SeedFile() => LoadSeedFile("seed/phase2-seed.json");

    private static SeedFile LoadSeedFile(string relativePath)
    {
        var path = FindRepoFile(relativePath);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SeedFile>(json, ProductManagerApiClient.JsonOptions)
            ?? throw new InvalidOperationException($"{relativePath} deserialized to null.");
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
    public void Every_feature_is_attributed_to_a_product_defined_in_the_market()
    {
        var seed = LoadSeedFile();
        var productNames = seed.Market!.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A feature is a capability of a product (see docs/PRDs/product_requirements.md) — the
        // seed should not leave any feature's productId null.
        Assert.All(seed.Features, f => Assert.False(string.IsNullOrWhiteSpace(f.ProductName), $"Feature '{f.Name}' has no productName."));
        Assert.All(seed.Features, f => Assert.Contains(f.ProductName!, productNames));
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

    // ---------- Phase 2: seed/phase2-seed.json, layered on top of phase 1 ----------

    [Fact]
    public void The_phase2_seed_file_parses_and_targets_the_phase1_market()
    {
        var phase1 = LoadSeedFile();
        var phase2 = LoadPhase2SeedFile();

        Assert.NotNull(phase2.MarketDynamics);
        Assert.Equal(phase1.Market!.Name, phase2.MarketDynamics!.MarketName);
    }

    [Fact]
    public void Every_segment_stock_state_covers_a_segment_defined_in_phase1_with_a_realistic_scale()
    {
        var phase1 = LoadSeedFile();
        var phase2 = LoadPhase2SeedFile();
        var segmentNames = phase1.Market!.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // One stock state per Phase 1 segment — the engine needs a starting point for every segment it simulates.
        Assert.Equal(segmentNames.Count, phase2.MarketDynamics!.SegmentStockStates.Count);

        foreach (var stockState in phase2.MarketDynamics.SegmentStockStates)
        {
            Assert.Contains(stockState.SegmentName, segmentNames);
            Assert.True(stockState.InitialProspectPool > 0, $"{stockState.SegmentName}: prospect pool should be positive.");
            Assert.True(stockState.InitialClientCount > 0, $"{stockState.SegmentName}: client count should be positive.");
            Assert.True(stockState.ArpuPerMonth > 0, $"{stockState.SegmentName}: ARPU should be positive.");
            Assert.Equal(2, stockState.ModulePenetrations.Count);
        }
    }

    [Fact]
    public void Every_module_penetration_and_cross_sell_parameter_references_a_module_product_defined_in_phase1()
    {
        var phase1 = LoadSeedFile();
        var phase2 = LoadPhase2SeedFile();
        var moduleNames = phase1.Market!.Products
            .Where(p => p.ParentProductName is not null)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(moduleNames.Count >= 2, "Expected at least two module products in the Phase 1 seed.");

        foreach (var stockState in phase2.MarketDynamics!.SegmentStockStates)
        foreach (var penetration in stockState.ModulePenetrations)
            Assert.Contains(penetration.ModuleProductName, moduleNames);

        var crossSellModules = phase2.MarketDynamics.AssumptionSets
            .SelectMany(a => a.Parameters)
            .Where(p => string.Equals(p.FlowKind, "CrossSellRate", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ModuleProductName)
            .ToList();

        Assert.All(crossSellModules, name => Assert.Contains(name!, moduleNames));
    }

    [Fact]
    public void The_baseline_assumption_set_covers_every_segment_by_non_cross_sell_flow_kind_plus_cross_sell_for_every_module()
    {
        var phase1 = LoadSeedFile();
        var phase2 = LoadPhase2SeedFile();
        var segmentNames = phase1.Market!.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moduleNames = phase1.Market.Products.Where(p => p.ParentProductName is not null).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var baseline = phase2.MarketDynamics!.AssumptionSets.Single(a => a.Name == "FY27 Baseline");

        string[] nonCrossSellFlows = ["NewFirmArrival", "ConversionToMarket", "WinRate", "ChurnHazard"];
        foreach (var segment in segmentNames)
        foreach (var flow in nonCrossSellFlows)
            Assert.Contains(baseline.Parameters, p =>
                string.Equals(p.SegmentName, segment, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.FlowKind, flow, StringComparison.OrdinalIgnoreCase));

        foreach (var segment in segmentNames)
        foreach (var module in moduleNames)
            Assert.Contains(baseline.Parameters, p =>
                string.Equals(p.SegmentName, segment, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.FlowKind, "CrossSellRate", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ModuleProductName, module, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(segmentNames.Count * (nonCrossSellFlows.Length + moduleNames.Count), baseline.Parameters.Count);
    }

    [Fact]
    public void Every_parameter_distribution_type_and_provenance_is_in_the_engine_vocabulary()
    {
        var phase2 = LoadPhase2SeedFile();
        string[] validDistTypes = ["Point", "Normal", "Lognormal", "Beta"];
        string[] validProvenance = ["Asserted", "Calibrated", "Default"];

        var parameters = phase2.MarketDynamics!.AssumptionSets.SelectMany(a => a.Parameters).ToList();
        Assert.All(parameters, p => Assert.Contains(p.DistType, validDistTypes));
        Assert.All(parameters, p => Assert.Contains(p.Provenance, validProvenance));

        // Distributions really do mix families, as the task asked for — not all Point or all Beta.
        var distinctTypes = parameters.Select(p => p.DistType).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(distinctTypes.Count >= 3, $"Expected a mix of distribution families, found only: {string.Join(", ", distinctTypes)}.");
    }

    [Fact]
    public void One_parameter_carries_a_covariate_link_to_a_covariate_defined_in_the_same_file()
    {
        var phase2 = LoadPhase2SeedFile();
        var covariateNames = phase2.MarketDynamics!.Covariates.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(covariateNames);

        var linked = phase2.MarketDynamics.AssumptionSets
            .SelectMany(a => a.Parameters)
            .Where(p => p.CovariateName is not null)
            .ToList();

        Assert.NotEmpty(linked);
        Assert.All(linked, p => Assert.Contains(p.CovariateName!, covariateNames));
        Assert.All(linked, p => Assert.NotNull(p.CovariateBeta));
    }

    [Fact]
    public void The_covariate_has_twelve_months_of_distinct_observed_history_and_a_future_gbm_spec()
    {
        var phase2 = LoadPhase2SeedFile();
        var covariate = phase2.MarketDynamics!.Covariates.Single(c => c.Name == "Equity Market Index");

        Assert.Equal(12, covariate.Observations.Count);
        Assert.Equal(covariate.Observations.Count, covariate.Observations.Select(o => o.EffectiveDate).Distinct().Count());
        Assert.All(covariate.Observations, o => Assert.True(DateOnly.TryParse(o.EffectiveDate, out _), $"'{o.EffectiveDate}' is not a valid date."));

        Assert.NotNull(covariate.GbmStart);
        Assert.NotNull(covariate.GbmDrift);
        Assert.NotNull(covariate.GbmVol);
    }

    [Fact]
    public void The_hypothetical_market_event_is_flagged_hypothetical_with_a_window_and_rate_multipliers()
    {
        var phase1 = LoadSeedFile();
        var phase2 = LoadPhase2SeedFile();
        var competitorNames = phase1.Market!.Competitors.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var marketEvent = phase2.MarketDynamics!.MarketEvents.Single(e => e.Name == "Competitor cloud-migration mandate");

        Assert.True(marketEvent.IsHypothetical);
        Assert.True(DateOnly.TryParse(marketEvent.StartDate, out var start));
        Assert.True(DateOnly.TryParse(marketEvent.EndDate, out var end));
        Assert.True(end > start);
        Assert.NotEmpty(marketEvent.RateMultipliers);
        if (marketEvent.CompetitorName is not null)
            Assert.Contains(marketEvent.CompetitorName, competitorNames);
    }
}
