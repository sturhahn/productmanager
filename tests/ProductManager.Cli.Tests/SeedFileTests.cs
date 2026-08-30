using System.Text.Json;
using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;
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

    private static SeedFile LoadPhase3SeedFile() => LoadSeedFile("seed/phase3-seed.json");

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

    // ---------- Phase 3: seed/phase3-seed.json, layered on top of phases 1-2 ----------

    [Fact]
    public void The_phase3_seed_file_parses_and_targets_the_phase1_market()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile();

        Assert.NotNull(phase3.MarketDynamics);
        Assert.Equal(phase1.Market!.Name, phase3.MarketDynamics!.MarketName);
    }

    [Fact]
    public void The_phase3_seed_file_matches_the_scale_described_in_the_task()
    {
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;

        Assert.Equal(2, phase3.Channels.Count);
        Assert.Equal(3, phase3.Integrations.Count);
        Assert.True(phase3.Firms.Count >= 28, $"Expected ~30 firms, found {phase3.Firms.Count}.");
        Assert.True(phase3.Opportunities.Count >= 30, $"Expected ~32 opportunities, found {phase3.Opportunities.Count}.");
        Assert.True(phase3.ChurnEvents.Count >= 6, $"Expected ~8 churn events, found {phase3.ChurnEvents.Count}.");
        Assert.NotEmpty(phase3.ArrivalCounts);
    }

    /// <summary>
    /// SeedLoader has no natural key for an opportunity, so it matches a firm's existing ones by
    /// (openedDate, scope) before creating. Two seed records sharing that triple are therefore
    /// indistinguishable to the loader: the second is silently absorbed and the file's advertised
    /// deal count quietly overstates what actually lands in the ledger — which then understates
    /// every calibration sample size drawn from it.
    /// </summary>
    [Fact]
    public void No_two_seed_opportunities_collide_on_the_key_the_loader_matches_them_by()
    {
        var opportunities = LoadPhase3SeedFile().MarketDynamics!.Opportunities;

        var duplicates = opportunities
            .GroupBy(o => (o.FirmName, o.OpenedDate, Scope: string.Join("|", o.ScopeProcessNames.OrderBy(n => n, StringComparer.Ordinal))))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.FirmName} / {g.Key.OpenedDate} / [{g.Key.Scope}] x{g.Count()}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "These opportunities share the loader's match key, so only the first of each would be created: "
            + string.Join("; ", duplicates));
    }

    /// <summary>
    /// The firm-level choice engine multiplies each product's effective coverage by the segment's
    /// importance weight for that process, and those weights come from Need rows. A seed with a
    /// coverage grid and no needs therefore simulates a market where nobody wants anything: every
    /// coverage term is zeroed, and the assumption set's importance-weight overrides — which only
    /// override an existing need's weight — never fire.
    /// </summary>
    [Fact]
    public void Every_segment_has_needs_and_every_importance_weight_parameter_has_one_to_override()
    {
        var market = LoadSeedFile().Market!;
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;

        var processNames = market.BusinessProcesses.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var segmentNames = market.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(market.Needs);
        Assert.All(market.Needs, n =>
        {
            Assert.Contains(n.SegmentName, segmentNames);
            Assert.Contains(n.BusinessProcessName, processNames);
            Assert.True(n.ImportanceWeight > 0, $"Need {n.SegmentName}/{n.BusinessProcessName} has a zero importance weight.");
            Assert.True(n.RequiredCoverage is null or >= 0 and <= 1);
        });

        foreach (var segment in market.Segments)
            Assert.Contains(market.Needs, n => string.Equals(n.SegmentName, segment.Name, StringComparison.OrdinalIgnoreCase));

        var needKeys = market.Needs
            .Select(n => (n.SegmentName.ToLowerInvariant(), n.BusinessProcessName.ToLowerInvariant()))
            .ToHashSet();

        // Each need is declared once: the loader matches on (segment, process), so a repeat would
        // be dropped rather than merged.
        Assert.Equal(market.Needs.Count, needKeys.Count);

        foreach (var parameter in phase3.AssumptionSets.SelectMany(a => a.Parameters)
                     .Where(p => p.ChoiceKind == "ImportanceWeight"))
        {
            Assert.Contains(
                (parameter.SegmentName!.ToLowerInvariant(), parameter.BusinessProcessName!.ToLowerInvariant()),
                needKeys);
        }
    }

    /// <summary>
    /// A product with no price in a segment is dropped from that segment's consideration set
    /// entirely (FirmLevelInputBuilder: "not sellable in this segment"). Unpriced competitors are
    /// therefore not weak competitors, they are absent ones — the funnel's Lost count collapses to
    /// zero and every loss is booked as a no-decision.
    /// </summary>
    [Fact]
    public void Every_product_including_the_competitors_carries_a_price()
    {
        var products = LoadSeedFile().Market!.Products;

        Assert.All(products, p =>
            Assert.True(p.Prices.Count > 0, $"Product '{p.Name}' has no price, so it can never enter a consideration set."));

        // At least one competitor product, or the choice model has nothing to lose to.
        Assert.Contains(products, p => p.CompetitorName is not null);
    }

    /// <summary>
    /// A module with no coverage covers nothing, so a cross-sell opportunity scoped to it can never
    /// be won — the seed's cross-sell deals would be unreproducible by the engine.
    /// </summary>
    [Fact]
    public void Every_module_named_by_a_cross_sell_opportunity_has_coverage()
    {
        var market = LoadSeedFile().Market!;
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;

        var covered = market.Coverages
            .Where(c => !string.Equals(c.Tier, "NotPlaying", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ProductName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var moduleName in phase3.Opportunities
                     .Select(o => o.CrossSellModuleProductName)
                     .Where(n => n is not null)
                     .Distinct())
        {
            Assert.True(covered.Contains(moduleName!),
                $"Cross-sell module '{moduleName}' has no coverage row, so it can never win an opportunity.");
        }
    }

    [Fact]
    public void Every_channel_segment_reach_and_product_affinity_reference_resolves_to_phase1_entities()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var segmentNames = phase1.Market!.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productNames = phase1.Market.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in phase3.Channels)
        {
            Assert.NotEmpty(channel.SegmentReaches);
            foreach (var reach in channel.SegmentReaches)
            {
                Assert.Contains(reach.SegmentName, segmentNames);
                Assert.True(reach.Reach is > 0 and <= 1, $"{channel.Name}/{reach.SegmentName}: reach should be in (0,1].");
            }

            foreach (var affinity in channel.ProductAffinities)
                Assert.Contains(affinity.ProductName, productNames);
        }
    }

    [Fact]
    public void Every_integration_edge_references_two_distinct_phase1_products_and_includes_the_partial_competitor_story()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var productNames = phase1.Market!.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var competitorProductNames = phase1.Market.Products
            .Where(p => p.CompetitorName is not null)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var integration in phase3.Integrations)
        {
            Assert.Contains(integration.ProductAName, productNames);
            Assert.Contains(integration.ProductBName, productNames);
            Assert.NotEqual(integration.ProductAName, integration.ProductBName, StringComparer.OrdinalIgnoreCase);
            Assert.True(integration.QualityScore is > 0 and <= 1, $"{integration.ProductAName}<->{integration.ProductBName}: quality score should be in (0,1].");
        }

        // The PRD's partial-competitor story: at least one integration bridges one of our products to a competitor's.
        Assert.Contains(phase3.Integrations, i =>
            competitorProductNames.Contains(i.ProductAName) || competitorProductNames.Contains(i.ProductBName));
    }

    [Fact]
    public void Every_firm_references_a_phase1_segment_and_its_relationships_reference_phase1_products_and_processes()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var segmentNames = phase1.Market!.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productNames = phase1.Market.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processNames = phase1.Market.BusinessProcesses.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] validStatuses = ["Observed", "Inferred", "Synthetic"];

        Assert.All(phase3.Firms, f => Assert.False(string.IsNullOrWhiteSpace(f.SegmentName), $"Firm '{f.Name}' has no segmentName."));
        Assert.All(phase3.Firms, f => Assert.Contains(f.SegmentName!, segmentNames));
        Assert.All(phase3.Firms, f => Assert.Contains(f.EpistemicStatus, validStatuses));

        foreach (var firm in phase3.Firms)
        foreach (var rel in firm.Relationships)
        {
            Assert.Contains(rel.ProductName, productNames);
            Assert.All(rel.ServedProcessNames, name => Assert.Contains(name, processNames));
        }

        // The task asks for a mix: our clients (with module holdings), competitor clients, unaffiliated
        // prospects, and a few marked inferred.
        var ourProductNames = phase1.Market.Products.Where(p => p.CompetitorName is null).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(phase3.Firms, f => f.Relationships.Any(r => ourProductNames.Contains(r.ProductName)) && f.Relationships.Count > 1);
        Assert.Contains(phase3.Firms, f => f.Relationships.Any(r => !ourProductNames.Contains(r.ProductName)));
        Assert.Contains(phase3.Firms, f => f.Relationships.Count == 0);
        Assert.Contains(phase3.Firms, f => f.EpistemicStatus == "Inferred");
    }

    [Fact]
    public void Firm_names_are_unique_within_the_phase3_seed_file()
    {
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var duplicates = phase3.Firms
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void The_baseline_assumption_set_gains_choice_parameters_using_the_engines_key_scheme()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var segmentNames = phase1.Market!.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processNames = phase1.Market.BusinessProcesses.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productNames = phase1.Market.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var baseline = phase3.AssumptionSets.Single(a => a.Name == "FY27 Baseline");
        var choiceParams = baseline.Parameters.Where(p => p.ChoiceKind is not null).ToList();
        Assert.NotEmpty(choiceParams);

        // Global scalars: no segment/process qualifier.
        Assert.Contains(choiceParams, p => p.ChoiceKind == "SwitchingCostBase");
        Assert.Contains(choiceParams, p => p.ChoiceKind == "ExpansionPropensity");

        // Sales-cycle lognormals keyed per segment (mu/sigma pair each).
        var salesCycleSegments = choiceParams
            .Where(p => p.ChoiceKind is "SalesCycleMu" or "SalesCycleSigma")
            .Select(p => p.SegmentName)
            .ToList();
        Assert.All(salesCycleSegments, name => Assert.Contains(name!, segmentNames));

        // Importance weights keyed per segment x process.
        var importanceParams = choiceParams.Where(p => p.ChoiceKind == "ImportanceWeight").ToList();
        Assert.NotEmpty(importanceParams);
        foreach (var p in importanceParams)
        {
            Assert.Contains(p.SegmentName!, segmentNames);
            Assert.Contains(p.BusinessProcessName!, processNames);
        }

        // At least one importance weight per phase1 segment (top processes for that segment).
        foreach (var segment in segmentNames)
            Assert.Contains(importanceParams, p => string.Equals(p.SegmentName, segment, StringComparison.OrdinalIgnoreCase));

        // Brand bias keyed per product x segment.
        var brandParams = choiceParams.Where(p => p.ChoiceKind == "BrandBias").ToList();
        Assert.NotEmpty(brandParams);
        foreach (var p in brandParams)
        {
            Assert.Contains(p.ModuleProductName!, productNames);
            Assert.Contains(p.SegmentName!, segmentNames);
        }
    }

    [Fact]
    public void Every_opportunity_references_a_phase3_firm_and_an_optional_phase3_channel()
    {
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var firmNames = phase3.Firms.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var channelNames = phase3.Channels.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(phase3.Opportunities, o => Assert.Contains(o.FirmName, firmNames));
        Assert.All(phase3.Opportunities.Where(o => o.ChannelName is not null),
            o => Assert.Contains(o.ChannelName!, channelNames));

        // A meaningful share of opportunities originate via a channel (not all "direct").
        Assert.True(phase3.Opportunities.Count(o => o.ChannelName is not null) >= phase3.Opportunities.Count / 4,
            "Expected at least a quarter of opportunities to carry a channel.");
    }

    [Fact]
    public void Every_opportunity_scope_and_product_reference_resolves_to_phase1_entities()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var processNames = phase1.Market!.BusinessProcesses.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productNames = phase1.Market.Products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] validOutcomes = ["Won", "Lost", "NoDecision"];

        foreach (var opp in phase3.Opportunities)
        {
            Assert.NotEmpty(opp.ScopeProcessNames);
            Assert.All(opp.ScopeProcessNames, name => Assert.Contains(name, processNames));
            Assert.Contains(opp.Outcome, validOutcomes);
            Assert.True(DateOnly.TryParse(opp.OpenedDate, out var opened), $"'{opp.OpenedDate}' is not a valid date.");
            Assert.True(DateOnly.TryParse(opp.DecidedDate, out var decided), $"'{opp.DecidedDate}' is not a valid date.");
            Assert.True(decided >= opened, $"Opportunity for '{opp.FirmName}' decided before it opened.");

            if (opp.WinningProductName is not null)
                Assert.Contains(opp.WinningProductName, productNames);
            if (opp.CrossSellModuleProductName is not null)
                Assert.Contains(opp.CrossSellModuleProductName, productNames);

            // A decided opportunity (Won or Lost) names whoever won — us or a competitor;
            // NoDecision must not claim a winner.
            if (opp.Outcome == "NoDecision")
                Assert.Null(opp.WinningProductName);
            else
                Assert.NotNull(opp.WinningProductName);
        }

        // Historical facts should span roughly the described window (2025-09..2026-08) and cover all three outcomes.
        var openedDates = phase3.Opportunities.Select(o => DateOnly.Parse(o.OpenedDate)).ToList();
        Assert.True(openedDates.Min() >= new DateOnly(2025, 9, 1));
        Assert.True(openedDates.Max() <= new DateOnly(2026, 8, 31));
        Assert.Contains(phase3.Opportunities, o => o.Outcome == "Won");
        Assert.Contains(phase3.Opportunities, o => o.Outcome == "Lost");
        Assert.Contains(phase3.Opportunities, o => o.Outcome == "NoDecision");
    }

    [Fact]
    public void Cross_sell_opportunities_carry_a_module_product_and_a_realistic_mix_of_outcomes()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var ourProductNames = phase1.Market!.Products.Where(p => p.CompetitorName is null).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var crossSells = phase3.Opportunities.Where(o => o.CrossSellModuleProductName is not null).ToList();

        // Cross-sell = a scope expansion at an existing client, so the module offered is always ours;
        // a cross-sell attempt can still lose or end with no decision, like any opportunity.
        Assert.NotEmpty(crossSells);
        Assert.All(crossSells, o => Assert.Contains(o.CrossSellModuleProductName!, ourProductNames));
        Assert.All(crossSells.Where(o => o.Outcome == "Won"), o => Assert.Equal(o.CrossSellModuleProductName, o.WinningProductName));
    }

    [Fact]
    public void Exactly_the_late_recorded_opportunities_have_a_recordedDate_materially_after_their_decidedDate()
    {
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var lateOnes = phase3.Opportunities.Where(o => o.LateRecorded).ToList();

        // The task asks for "3 facts recorded LATE — after their effective month" so as-of tests bite.
        Assert.Equal(3, lateOnes.Count);

        foreach (var opp in phase3.Opportunities)
        {
            Assert.NotNull(opp.RecordedDate);
            var decided = DateOnly.Parse(opp.DecidedDate);
            var recorded = DateOnly.Parse(opp.RecordedDate!);

            if (opp.LateRecorded)
            {
                // "After their effective month": recorded in a later calendar month than decided, by a real margin.
                Assert.True(recorded > decided.AddDays(20),
                    $"'{opp.FirmName}' is flagged LateRecorded but recordedDate {recorded} is not materially after decidedDate {decided}.");
            }
            else
            {
                Assert.True(recorded <= decided.AddDays(20),
                    $"'{opp.FirmName}' is not flagged LateRecorded but recordedDate {recorded} is materially after decidedDate {decided}.");
            }
        }
    }

    [Fact]
    public void Every_churn_event_references_a_phase1_segment_and_has_a_recorded_date()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var segmentNames = phase1.Market!.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(phase3.ChurnEvents, c => Assert.Contains(c.SegmentName, segmentNames));
        Assert.All(phase3.ChurnEvents, c => Assert.True(DateOnly.TryParse(c.EffectiveDate, out _), $"'{c.EffectiveDate}' is not a valid date."));
    }

    [Fact]
    public void Every_arrival_count_references_a_phase1_segment_uses_a_valid_flow_kind_and_has_a_positive_count()
    {
        var phase1 = LoadSeedFile();
        var phase3 = LoadPhase3SeedFile().MarketDynamics!;
        var segmentNames = phase1.Market!.Segments.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] validFlowKinds = ["NewFirmArrival", "ConversionToMarket"];

        Assert.All(phase3.ArrivalCounts, a => Assert.Contains(a.SegmentName, segmentNames));
        Assert.All(phase3.ArrivalCounts, a => Assert.Contains(a.FlowKind, validFlowKinds));
        Assert.All(phase3.ArrivalCounts, a => Assert.True(a.Count >= 0, $"{a.SegmentName}/{a.Month}: count should not be negative."));
        Assert.All(phase3.ArrivalCounts, a => Assert.True(a.Exposure > 0, $"{a.SegmentName}/{a.Month}: exposure should be positive."));
        Assert.All(phase3.ArrivalCounts, a => Assert.True(DateOnly.TryParse(a.Month, out _), $"'{a.Month}' is not a valid month date."));

        // ConversionToMarket exposure is the prospect pool size, so it should generally exceed the
        // NewFirmArrival exposure of 1/month.
        Assert.Contains(phase3.ArrivalCounts, a => a.FlowKind == "ConversionToMarket" && a.Exposure > 1.0);
    }
}
