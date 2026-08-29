using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Seed;

/// <summary>
/// Loads a seed file through the API, the only write path. Every top-level entity is matched by
/// natural key (name, scoped to its parent) before creating, so re-running the same file against
/// an already-seeded workspace does not duplicate anything. Sub-resources that the API already
/// upserts (team/period capacity, project effort demand, dependency edges, delivered-feature
/// links, roadmap plan links) are simply (re)applied every run — no extra bookkeeping needed.
/// </summary>
public static class SeedLoader
{
    private const int ListPageSize = 200; // generous for a seed file's realistic scale; see remarks below.

    public static async Task<SeedTally> RunAsync(ProductManagerApiClient client, SeedFile seed, CancellationToken ct)
    {
        var tally = new SeedTally();

        var workspaceId = await LoadWorkspaceAsync(client, seed.Workspace, tally, ct);

        var market = seed.Market is null
            ? null
            : await LoadMarketAsync(client, workspaceId, seed.Market, tally, ct);

        if (seed.MarketDynamics is not null)
            await LoadMarketDynamicsAsync(client, workspaceId, seed.MarketDynamics, tally, ct);

        var teamIndex = await LoadTeamsAsync(client, workspaceId, seed.Teams, tally, ct);
        var initiativeIndex = await LoadInitiativesAsync(client, workspaceId, seed.Initiatives, market?.Segments, tally, ct);
        var featureIndex = await LoadFeaturesAsync(client, workspaceId, seed.Features, market, tally, ct);
        var projectIndex = await LoadProjectsAsync(client, seed.Projects, initiativeIndex, teamIndex, featureIndex, tally, ct);

        if (seed.Roadmap is not null)
            await LoadRoadmapAsync(client, workspaceId, seed.Roadmap, projectIndex, tally, ct);

        return tally;
    }

    // ---------- Workspace ----------

    private static async Task<Guid> LoadWorkspaceAsync(ProductManagerApiClient client, SeedWorkspace? seed, SeedTally tally, CancellationToken ct)
    {
        if (seed is null || string.IsNullOrWhiteSpace(seed.Name))
        {
            var defaultWorkspace = await client.GetDefaultWorkspaceAsync(ct);
            tally.RecordMatched("workspace");
            return defaultWorkspace.Id;
        }

        var existing = await client.ListWorkspacesAsync(1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(w => (w.Name, w.Id)));
        if (index.TryGet(seed.Name, out var id))
        {
            tally.RecordMatched("workspace");
            return id;
        }

        // Phase 1 is single-workspace: the API bootstraps one workspace on first run and the
        // frontend always resolves it through GET /api/workspaces/default (the oldest one).
        // Creating a second workspace here would load the whole seed into a workspace the UI
        // never shows, so adopt the bootstrap workspace and give it the seed's name instead.
        var target = await client.GetDefaultWorkspaceAsync(ct);
        await client.UpdateWorkspaceAsync(target.Id, new UpdateWorkspaceRequest(seed.Name), ct);
        tally.RecordCreated("workspace");
        return target.Id;
    }

    // ---------- Teams ----------

    private static async Task<NameIndex> LoadTeamsAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedTeam> teams, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListTeamsAsync(workspaceId, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(t => (t.Name, t.Id)));

        foreach (var team in teams)
        {
            var id = await GetOrCreateAsync(index, team.Name, "team", tally,
                async () => (await client.CreateTeamAsync(new CreateTeamRequest(workspaceId, team.Name, team.SpecializationTag), ct)).Id);

            foreach (var capacity in team.Capacities)
                await client.SetTeamCapacityAsync(id, new SetTeamCapacityRequest(capacity.Year, capacity.Month, capacity.Points), ct);
        }

        return index;
    }

    // ---------- Initiatives ----------

    private static async Task<NameIndex> LoadInitiativesAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedInitiative> initiatives,
        NameIndex? segmentIndex, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListInitiativesAsync(workspaceId, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(i => (i.Name, i.Id)));

        foreach (var initiative in initiatives)
        {
            var segmentIds = initiative.TargetSegmentNames.Count == 0
                ? null
                : initiative.TargetSegmentNames.Select(n => ResolveSegment(segmentIndex, n)).ToList();

            await GetOrCreateAsync(index, initiative.Name, "initiative", tally, async () =>
                (await client.CreateInitiativeAsync(
                    new CreateInitiativeRequest(workspaceId, initiative.Name, initiative.Outcome, initiative.HypothesizedValue,
                        segmentIds, TargetNeedIds: null), ct)).Id);
        }

        return index;
    }

    private static Guid ResolveSegment(NameIndex? segmentIndex, string name) =>
        segmentIndex is null
            ? throw new SeedReferenceException($"segment '{name}' (no market/segments defined in the seed file)")
            : segmentIndex.GetOrThrow(name);

    // ---------- Features ----------

    private static async Task<NameIndex> LoadFeaturesAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedFeature> features, MarketIndexes? market,
        SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListFeaturesAsync(workspaceId, null, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(f => (f.Name, f.Id)));

        foreach (var feature in features)
        {
            Guid? productId = feature.ProductName is null
                ? null
                : RequireMarket(market).Products.GetOrThrow(feature.ProductName);

            var id = await GetOrCreateAsync(index, feature.Name, "feature", tally, async () =>
                (await client.CreateFeatureAsync(new CreateFeatureRequest(workspaceId, feature.Name, feature.Description, productId), ct)).Id);

            if (feature.Status is not null)
            {
                var status = ParseHelpers.ParseEnum<FeatureStatus>(feature.Status, "feature status");
                var shippedDate = ParseHelpers.ParseOptionalDate(feature.ShippedDate, "feature shippedDate");
                await client.UpdateFeatureAsync(id, new UpdateFeatureRequest(feature.Name, feature.Description, productId, status, shippedDate), ct);
            }

            if (feature.Deltas.Count > 0)
            {
                var deltaRequests = feature.Deltas.Select(d => ToDeltaRequest(d, market)).ToList();
                // Replace-all is idempotent regardless of what the feature already carries.
                await client.ReplaceFeatureDeltasAsync(id, deltaRequests, ct);
            }
        }

        return index;
    }

    private static AddFeatureDeltaRequest ToDeltaRequest(SeedFeatureDelta delta, MarketIndexes? market)
    {
        var kind = ParseHelpers.ParseEnum<FeatureDeltaKind>(delta.Kind, "delta kind");

        Guid? businessProcessId = delta.BusinessProcessName is null
            ? null
            : RequireMarket(market).BusinessProcesses.GetOrThrow(delta.BusinessProcessName);

        Guid? approachId = delta.ApproachName is null || delta.BusinessProcessName is null
            ? null
            : RequireMarket(market).Approaches.GetOrThrow(ApproachKey(delta.BusinessProcessName, delta.ApproachName));

        Guid? segmentId = delta.SegmentName is null
            ? null
            : RequireMarket(market).Segments.GetOrThrow(delta.SegmentName);

        return new AddFeatureDeltaRequest(
            kind,
            businessProcessId,
            ParseHelpers.ParseOptionalEnum<CoverageTier>(delta.FromTier, "delta fromTier"),
            ParseHelpers.ParseOptionalEnum<CoverageTier>(delta.ToTier, "delta toTier"),
            approachId,
            segmentId,
            delta.Magnitude,
            delta.Rationale);
    }

    // ---------- Projects ----------

    private static async Task<NameIndex> LoadProjectsAsync(
        ProductManagerApiClient client, List<SeedProject> projects,
        NameIndex initiativeIndex, NameIndex teamIndex, NameIndex featureIndex, SeedTally tally, CancellationToken ct)
    {
        var index = new NameIndex();
        // Projects are scoped to an initiative in the API, so list per-initiative once each,
        // rather than a workspace-wide listing keyed only by name.
        var listedInitiatives = new HashSet<Guid>();

        foreach (var project in projects)
        {
            var initiativeId = initiativeIndex.GetOrThrow(project.InitiativeName);

            if (listedInitiatives.Add(initiativeId))
            {
                var existing = await client.ListProjectsAsync(null, initiativeId, 1, ListPageSize, ct);
                foreach (var p in existing.Items)
                    index.Add(p.Name, p.Id);
            }

            var status = ParseHelpers.ParseOptionalEnum<ProjectStatus>(project.Status, "project status");
            var startDate = ParseHelpers.ParseOptionalDate(project.StartDate, "project startDate");
            var endDate = ParseHelpers.ParseOptionalDate(project.EndDate, "project endDate");

            await GetOrCreateAsync(index, project.Name, "project", tally, async () =>
                (await client.CreateProjectAsync(
                    new CreateProjectRequest(initiativeId, project.Name, project.Description, status, startDate, endDate), ct)).Id);
        }

        // Dependencies and delivered features reference other projects/features, so resolve them
        // in a second pass once every project in this file has an id.
        foreach (var project in projects)
        {
            var id = index.GetOrThrow(project.Name);

            foreach (var dependsOnName in project.DependsOnProjectNames)
                await client.AddProjectDependencyAsync(id, index.GetOrThrow(dependsOnName), ct);

            foreach (var demand in project.EffortDemands)
                await client.SetEffortDemandAsync(id,
                    new SetEffortDemandRequest(teamIndex.GetOrThrow(demand.TeamName), demand.Year, demand.Month, demand.Points), ct);

            foreach (var featureName in project.DeliversFeatureNames)
                await client.LinkFeatureAsync(id, featureIndex.GetOrThrow(featureName), ct);
        }

        return index;
    }

    // ---------- Roadmap ----------

    private static async Task LoadRoadmapAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedRoadmap roadmap, NameIndex projectIndex, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListRoadmapsAsync(workspaceId, null, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(r => (r.Name, r.Id)));

        var id = await GetOrCreateAsync(index, roadmap.Name, "roadmap", tally, async () =>
            (await client.CreateRoadmapAsync(new CreateRoadmapRequest(workspaceId, roadmap.Name, roadmap.VersionName, IsLive: false), ct)).Id);

        foreach (var item in roadmap.Items)
        {
            var projectId = projectIndex.GetOrThrow(item.ProjectName);
            var start = ParseHelpers.ParseDate(item.Start, "roadmap item start");
            var end = ParseHelpers.ParseDate(item.End, "roadmap item end");
            // PlanProject is upsert (re-plans if already present), so this is safe to repeat.
            await client.PlanProjectAsync(id, new PlanProjectRequest(projectId, start, end, item.SortOrder), ct);
        }

        if (roadmap.IsLive)
            await client.MakeRoadmapLiveAsync(id, ct);
    }

    // ---------- Market ----------

    private static async Task<MarketIndexes> LoadMarketAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedMarket seed, SeedTally tally, CancellationToken ct)
    {
        var existingMarkets = await client.ListMarketsAsync(workspaceId, 1, ListPageSize, ct);
        var marketIndex = new NameIndex(existingMarkets.Items.Select(m => (m.Name, m.Id)));
        var marketId = await GetOrCreateAsync(marketIndex, seed.Name, "market", tally, async () =>
            (await client.CreateMarketAsync(new CreateMarketRequest(workspaceId, seed.Name, seed.Description), ct)).Id);

        var segments = await LoadSegmentsAsync(client, marketId, seed.Segments, tally, ct);
        var businessProcesses = await LoadBusinessProcessesAsync(client, marketId, seed.BusinessProcesses, tally, ct);
        var approaches = await LoadApproachesAsync(client, businessProcesses, seed.Approaches, tally, ct);
        var competitors = await LoadCompetitorsAsync(client, marketId, seed.Competitors, tally, ct);
        var products = await LoadProductsAsync(client, marketId, seed.Products, competitors, segments, tally, ct);
        await LoadCoveragesAsync(client, seed.Coverages, products, businessProcesses, approaches, segments, tally, ct);

        return new MarketIndexes(marketId, segments, businessProcesses, approaches, competitors, products);
    }

    private static async Task<NameIndex> LoadSegmentsAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedSegment> segments, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListClientSegmentsAsync(marketId, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(s => (s.Name, s.Id)));

        foreach (var segment in segments)
            await GetOrCreateAsync(index, segment.Name, "clientSegment", tally, async () =>
                (await client.CreateClientSegmentAsync(
                    new CreateClientSegmentRequest(marketId, segment.Name, segment.Description, segment.SuiteAffinity, segment.ConsolidationPreference), ct)).Id);

        return index;
    }

    private static async Task<NameIndex> LoadBusinessProcessesAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedBusinessProcess> processes, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListBusinessProcessesAsync(marketId, null, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(p => (p.Name, p.Id)));

        // Parents must appear before their children in the seed file — the natural authoring order.
        foreach (var process in processes)
        {
            Guid? parentId = process.ParentProcessName is null ? null : index.GetOrThrow(process.ParentProcessName);
            await GetOrCreateAsync(index, process.Name, "businessProcess", tally, async () =>
                (await client.CreateBusinessProcessAsync(
                    new CreateBusinessProcessRequest(marketId, process.Name, process.Description, parentId), ct)).Id);
        }

        return index;
    }

    private static async Task<NameIndex> LoadApproachesAsync(
        ProductManagerApiClient client, NameIndex businessProcesses, List<SeedApproach> approaches, SeedTally tally, CancellationToken ct)
    {
        var index = new NameIndex();
        var listedProcesses = new HashSet<Guid>();

        foreach (var approach in approaches)
        {
            var businessProcessId = businessProcesses.GetOrThrow(approach.BusinessProcessName);
            if (listedProcesses.Add(businessProcessId))
            {
                var existing = await client.ListApproachesAsync(businessProcessId, 1, ListPageSize, ct);
                foreach (var a in existing.Items)
                    index.Add(ApproachKey(approach.BusinessProcessName, a.Name), a.Id);
            }

            var key = ApproachKey(approach.BusinessProcessName, approach.Name);
            await GetOrCreateAsync(index, key, "approach", tally, async () =>
                (await client.CreateApproachAsync(new CreateApproachRequest(businessProcessId, approach.Name, approach.Description), ct)).Id);
        }

        return index;
    }

    private static string ApproachKey(string businessProcessName, string approachName) =>
        $"{businessProcessName.Trim()}::{approachName.Trim()}";

    private static async Task<NameIndex> LoadCompetitorsAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedCompetitor> competitors, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListCompetitorsAsync(marketId, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(c => (c.Name, c.Id)));

        foreach (var competitor in competitors)
            await GetOrCreateAsync(index, competitor.Name, "competitor", tally, async () =>
                (await client.CreateCompetitorAsync(new CreateCompetitorRequest(marketId, competitor.Name, competitor.StrategyNotes), ct)).Id);

        return index;
    }

    private static async Task<NameIndex> LoadProductsAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedProduct> products,
        NameIndex competitors, NameIndex segments, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListProductsAsync(marketId, null, null, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(p => (p.Name, p.Id)));

        // Parent products (for modules) must appear before their modules in the seed file.
        foreach (var product in products)
        {
            Guid? competitorId = product.CompetitorName is null ? null : competitors.GetOrThrow(product.CompetitorName);
            Guid? parentProductId = product.ParentProductName is null ? null : index.GetOrThrow(product.ParentProductName);

            var id = await GetOrCreateAsync(index, product.Name, "product", tally, async () =>
                (await client.CreateProductAsync(
                    new CreateProductRequest(marketId, product.Name, product.Description, competitorId, parentProductId), ct)).Id);

            foreach (var price in product.Prices)
            {
                Guid? segmentId = price.SegmentName is null ? null : segments.GetOrThrow(price.SegmentName);
                await client.SetProductPriceAsync(id, new SetProductPriceRequest(
                    new MoneyDto(price.RecurringAmount, price.Currency),
                    new MoneyDto(price.ImplementationFeeAmount, price.ImplementationFeeCurrency ?? price.Currency),
                    segmentId,
                    price.UsageBasedPlaceholder), ct);
            }
        }

        return index;
    }

    private static async Task LoadCoveragesAsync(
        ProductManagerApiClient client, List<SeedCoverage> coverages,
        NameIndex products, NameIndex businessProcesses, NameIndex approaches, NameIndex segments,
        SeedTally tally, CancellationToken ct)
    {
        foreach (var coverage in coverages)
        {
            var productId = products.GetOrThrow(coverage.ProductName);
            var businessProcessId = businessProcesses.GetOrThrow(coverage.BusinessProcessName);
            Guid? approachId = coverage.ApproachName is null
                ? null
                : approaches.GetOrThrow(ApproachKey(coverage.BusinessProcessName, coverage.ApproachName));
            Guid? segmentId = coverage.SegmentName is null ? null : segments.GetOrThrow(coverage.SegmentName);

            // No upsert endpoint for coverage — match the existing assessment for this
            // (product, process, segment) by hand before creating a duplicate.
            var existing = await client.ListCoveragesAsync(productId, businessProcessId, segmentId, 1, ListPageSize, ct);
            if (existing.Items.Any(c => c.ApproachId == approachId))
            {
                tally.RecordMatched("coverage");
                continue;
            }

            var tier = ParseHelpers.ParseEnum<CoverageTier>(coverage.Tier, "coverage tier");
            await client.CreateCoverageAsync(new CreateCoverageRequest(
                productId, businessProcessId, tier, coverage.IsUnknown, coverage.Confidence,
                approachId, segmentId, ParseHelpers.ParseOptionalDate(coverage.EffectiveDate, "coverage effectiveDate"), coverage.Rationale), ct);
            tally.RecordCreated("coverage");
        }
    }

    // ---------- Market dynamics (Phase 2) ----------

    private static async Task LoadMarketDynamicsAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedMarketDynamics dynamics, SeedTally tally, CancellationToken ct)
    {
        var existingMarkets = await client.ListMarketsAsync(workspaceId, 1, ListPageSize, ct);
        var marketIndex = new NameIndex(existingMarkets.Items.Select(m => (m.Name, m.Id)));
        var marketId = marketIndex.GetOrThrow(dynamics.MarketName);

        var existingSegments = await client.ListClientSegmentsAsync(marketId, 1, ListPageSize, ct);
        var segmentIndex = new NameIndex(existingSegments.Items.Select(s => (s.Name, s.Id)));

        var existingProducts = await client.ListProductsAsync(marketId, null, null, 1, ListPageSize, ct);
        var productIndex = new NameIndex(existingProducts.Items.Select(p => (p.Name, p.Id)));

        var existingCompetitors = await client.ListCompetitorsAsync(marketId, 1, ListPageSize, ct);
        var competitorIndex = new NameIndex(existingCompetitors.Items.Select(c => (c.Name, c.Id)));

        foreach (var stockState in dynamics.SegmentStockStates)
            await ApplySegmentStockStateAsync(client, segmentIndex, productIndex, stockState, tally, ct);

        var covariateIndex = await LoadCovariatesAsync(client, workspaceId, dynamics.Covariates, tally, ct);

        foreach (var assumptionSet in dynamics.AssumptionSets)
            await LoadAssumptionSetAsync(client, workspaceId, assumptionSet, segmentIndex, productIndex, covariateIndex, tally, ct);

        foreach (var marketEvent in dynamics.MarketEvents)
            await LoadMarketEventAsync(client, marketId, marketEvent, segmentIndex, competitorIndex, tally, ct);
    }

    private static async Task ApplySegmentStockStateAsync(
        ProductManagerApiClient client, NameIndex segments, NameIndex products, SeedSegmentStockState stockState,
        SeedTally tally, CancellationToken ct)
    {
        var segmentId = segments.GetOrThrow(stockState.SegmentName);

        // SetStockState replaces the whole measurement, so re-applying the same seed value is a no-op
        // — safe to run on every load, the same way team capacities are simply re-applied every run.
        // There is no create-vs-match decision here, so these count as "applied" in the tally.
        await client.SetSegmentStockStateAsync(segmentId, new SetSegmentStockStateRequest(
            stockState.InitialProspectPool, stockState.InitialClientCount, stockState.ArpuPerMonth,
            stockState.ImplementationFee, stockState.ServicingCostPerClient, stockState.InfraFixedCostPerMonth,
            stockState.InfraCostPerClient, stockState.RdDragPerMonth), ct);
        tally.RecordApplied("segmentStock");

        foreach (var penetration in stockState.ModulePenetrations)
        {
            var moduleProductId = products.GetOrThrow(penetration.ModuleProductName);
            await client.SetSegmentModulePenetrationAsync(segmentId,
                new SetSegmentModulePenetrationRequest(moduleProductId, penetration.Penetration, penetration.ArpuPerMonth), ct);
            tally.RecordApplied("modulePenetration");
        }
    }

    private static async Task<NameIndex> LoadCovariatesAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedCovariate> covariates, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListCovariatesAsync(workspaceId, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(c => (c.Name, c.Id)));

        foreach (var covariate in covariates)
        {
            var id = await GetOrCreateAsync(index, covariate.Name, "covariate", tally, async () =>
                (await client.CreateCovariateAsync(new CreateCovariateRequest(
                    workspaceId, covariate.Name, covariate.Description, covariate.Unit,
                    covariate.GbmStart, covariate.GbmDrift, covariate.GbmVol), ct)).Id);

            foreach (var observation in covariate.Observations)
                await ApplyCovariateObservationAsync(client, workspaceId, id, observation, tally, ct);
        }

        return index;
    }

    private static async Task ApplyCovariateObservationAsync(
        ProductManagerApiClient client, Guid workspaceId, Guid covariateId, SeedCovariateObservation observation, SeedTally tally, CancellationToken ct)
    {
        var effectiveDate = ParseHelpers.ParseDate(observation.EffectiveDate, "covariate observation effectiveDate");

        // The facts ledger is append-only (no natural-key upsert on the API side), so idempotency is
        // the seed loader's job here: skip a date that already has a reading before appending another.
        var existing = await client.GetCovariateHistoryAsync(covariateId, effectiveDate, effectiveDate, null, ct);
        if (existing.Observations.Count > 0)
        {
            tally.RecordMatched("covariateObservation");
            return;
        }

        var payload = $"{{\"value\":{observation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        await client.CreateFactAsync(new CreateFactEventRequest(
            workspaceId, FactEventKind.CovariateObserved, effectiveDate,
            SubjectType: "Covariate", SubjectId: covariateId, PayloadJson: payload, Notes: observation.Notes), ct);
        tally.RecordCreated("covariateObservation");
    }

    private static async Task LoadAssumptionSetAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedAssumptionSet assumptionSet,
        NameIndex segments, NameIndex products, NameIndex covariates, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListAssumptionSetsAsync(workspaceId, null, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(a => (a.Name, a.Id)));

        var id = await GetOrCreateAsync(index, assumptionSet.Name, "assumptionSet", tally, async () =>
            (await client.CreateAssumptionSetAsync(new CreateAssumptionSetRequest(workspaceId, assumptionSet.Name, assumptionSet.Notes), ct)).Id);

        // UpsertParameter is upsert-by-engine-key (or by free-form Key) on the API side, so
        // re-applying every parameter on every load is safe — same idempotency story as capacities.
        foreach (var parameter in assumptionSet.Parameters)
        {
            Guid? segmentId = parameter.SegmentName is null ? null : segments.GetOrThrow(parameter.SegmentName);
            Guid? moduleProductId = parameter.ModuleProductName is null ? null : products.GetOrThrow(parameter.ModuleProductName);
            Guid? covariateId = parameter.CovariateName is null ? null : covariates.GetOrThrow(parameter.CovariateName);
            SimulationFlowKind? flowKind = parameter.FlowKind is null
                ? null
                : ParseHelpers.ParseEnum<SimulationFlowKind>(parameter.FlowKind, "parameter flowKind");

            var distribution = new DistributionSpecDto(
                ParseHelpers.ParseEnum<DistributionType>(parameter.DistType, "parameter distType"), parameter.P1, parameter.P2);
            var provenance = ParseHelpers.ParseEnum<ParameterProvenance>(parameter.Provenance, "parameter provenance");

            await client.UpsertParameterAsync(id, new UpsertParameterRequest(
                distribution, provenance, parameter.Rationale, segmentId, flowKind, moduleProductId,
                covariateId, parameter.CovariateBeta, parameter.Key), ct);
        }
    }

    private static async Task LoadMarketEventAsync(
        ProductManagerApiClient client, Guid marketId, SeedMarketEvent marketEvent,
        NameIndex segments, NameIndex competitors, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListMarketEventsAsync(marketId, null, null, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(e => (e.Name, e.Id)));

        if (index.TryGet(marketEvent.Name, out _))
        {
            tally.RecordMatched("marketEvent");
            return;
        }

        var competitorId = marketEvent.CompetitorName is null ? (Guid?)null : competitors.GetOrThrow(marketEvent.CompetitorName);
        var rateMultipliers = marketEvent.RateMultipliers.Select(m => new EventRateMultiplierDto(
            m.SegmentName is null ? (Guid?)null : segments.GetOrThrow(m.SegmentName),
            ParseHelpers.ParseEnum<SimulationFlowKind>(m.FlowKind, "market event rate multiplier flowKind"),
            m.Multiplier)).ToList();

        await client.CreateMarketEventAsync(new CreateMarketEventRequest(
            marketId,
            ParseHelpers.ParseEnum<MarketEventType>(marketEvent.Type, "market event type"),
            marketEvent.Name,
            ParseHelpers.ParseDate(marketEvent.StartDate, "market event startDate"),
            ParseHelpers.ParseOptionalDate(marketEvent.EndDate, "market event endDate"),
            marketEvent.IsHypothetical,
            marketEvent.Magnitude,
            competitorId,
            marketEvent.Notes,
            rateMultipliers), ct);
        tally.RecordCreated("marketEvent");
    }

    private static MarketIndexes RequireMarket(MarketIndexes? market) =>
        market ?? throw new SeedReferenceException("a market entity (no market defined in the seed file)");

    private static async Task<Guid> GetOrCreateAsync(
        NameIndex index, string name, string kind, SeedTally tally, Func<Task<Guid>> create)
    {
        if (index.TryGet(name, out var id))
        {
            tally.RecordMatched(kind);
            return id;
        }

        var newId = await create();
        index.Add(name, newId);
        tally.RecordCreated(kind);
        return newId;
    }

    private sealed record MarketIndexes(
        Guid MarketId, NameIndex Segments, NameIndex BusinessProcesses, NameIndex Approaches, NameIndex Competitors, NameIndex Products);
}
