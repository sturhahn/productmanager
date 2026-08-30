using System.Globalization;
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
        await LoadNeedsAsync(client, seed.Needs, businessProcesses, segments, tally, ct);

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

    /// <summary>
    /// Loads segment-level demand edges. Like coverage there is no upsert endpoint, so an existing
    /// need for the same (segment, process) is matched rather than duplicated — importance weights
    /// would otherwise double up silently on a re-run.
    /// </summary>
    private static async Task LoadNeedsAsync(
        ProductManagerApiClient client, List<SeedNeed> needs,
        NameIndex businessProcesses, NameIndex segments, SeedTally tally, CancellationToken ct)
    {
        foreach (var need in needs)
        {
            var segmentId = segments.GetOrThrow(need.SegmentName);
            var businessProcessId = businessProcesses.GetOrThrow(need.BusinessProcessName);

            var existing = await client.ListNeedsAsync(null, segmentId, businessProcessId, 1, ListPageSize, ct);
            if (existing.Items.Count > 0)
            {
                tally.RecordMatched("need");
                continue;
            }

            if (need.RequiredCoverage is < 0 or > 1)
                throw new ArgumentException(
                    $"need '{need.SegmentName}' / '{need.BusinessProcessName}' has requiredCoverage " +
                    $"{need.RequiredCoverage} — it must be between 0 and 1.");

            // The coverage floor rides in the need's requiredDepthJson side-channel, which is where
            // FirmLevelInputBuilder reads it from (industry specifics as data, not schema).
            string? requiredDepthJson = need.RequiredCoverage is null
                ? null
                : $"{{\"requiredCoverage\":{need.RequiredCoverage.Value.ToString(CultureInfo.InvariantCulture)}}}";

            await client.CreateNeedAsync(new CreateNeedRequest(
                businessProcessId,
                need.ImportanceWeight,
                FirmId: null,
                ClientSegmentId: segmentId,
                IsHard: need.IsHard,
                WorkaroundCostPerPeriod: need.WorkaroundCostPerPeriod is null
                    ? null
                    : new MoneyDto((decimal)need.WorkaroundCostPerPeriod.Value, "USD"),
                RequiredDepthJson: requiredDepthJson,
                Conditionality: need.Conditionality,
                Notes: need.Notes), ct);
            tally.RecordCreated("need");
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

        var existingProcesses = await client.ListBusinessProcessesAsync(marketId, null, 1, ListPageSize, ct);
        var processIndex = new NameIndex(existingProcesses.Items.Select(p => (p.Name, p.Id)));

        foreach (var stockState in dynamics.SegmentStockStates)
            await ApplySegmentStockStateAsync(client, segmentIndex, productIndex, stockState, tally, ct);

        var covariateIndex = await LoadCovariatesAsync(client, workspaceId, dynamics.Covariates, tally, ct);

        foreach (var assumptionSet in dynamics.AssumptionSets)
            await LoadAssumptionSetAsync(client, workspaceId, assumptionSet, segmentIndex, productIndex, processIndex, covariateIndex, tally, ct);

        foreach (var marketEvent in dynamics.MarketEvents)
            await LoadMarketEventAsync(client, marketId, marketEvent, segmentIndex, competitorIndex, tally, ct);

        // ---------- Phase 3 ----------

        var channelIndex = await LoadChannelsAsync(client, marketId, dynamics.Channels, segmentIndex, productIndex, tally, ct);
        await LoadIntegrationsAsync(client, dynamics.Integrations, productIndex, tally, ct);
        var firmIndex = await LoadFirmsAsync(client, marketId, dynamics.Firms, segmentIndex, productIndex, processIndex, tally, ct);
        await LoadOpportunitiesAsync(client, dynamics.Opportunities, firmIndex, channelIndex, productIndex, processIndex, tally, ct);
        await LoadChurnEventsAsync(client, workspaceId, dynamics.ChurnEvents, segmentIndex, tally, ct);
        await LoadArrivalCountsAsync(client, workspaceId, dynamics.ArrivalCounts, segmentIndex, tally, ct);
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
        var recordedAt = ToUtcMidnight(ParseHelpers.ParseOptionalDate(observation.RecordedDate, "covariate observation recordedDate"));
        await client.CreateFactAsync(new CreateFactEventRequest(
            workspaceId, FactEventKind.CovariateObserved, effectiveDate,
            SubjectType: "Covariate", SubjectId: covariateId, PayloadJson: payload, Notes: observation.Notes,
            RecordedAt: recordedAt), ct);
        tally.RecordCreated("covariateObservation");
    }

    private static async Task LoadAssumptionSetAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedAssumptionSet assumptionSet,
        NameIndex segments, NameIndex products, NameIndex processes, NameIndex covariates, SeedTally tally, CancellationToken ct)
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

            var distribution = new DistributionSpecDto(
                ParseHelpers.ParseEnum<DistributionType>(parameter.DistType, "parameter distType"), parameter.P1, parameter.P2);
            var provenance = ParseHelpers.ParseEnum<ParameterProvenance>(parameter.Provenance, "parameter provenance");

            if (parameter.ChoiceKind is not null)
            {
                // A choice-model parameter: keyed via ChoiceParameterKeys, always free-form on the
                // wire (segmentId/flowKind stay null so the API treats it as a Key parameter, not an
                // engine one — see ParameterKeyHelper.Validate).
                Guid? processId = parameter.BusinessProcessName is null ? null : processes.GetOrThrow(parameter.BusinessProcessName);
                var key = ChoiceParameterKeys.Build(parameter.ChoiceKind, segmentId, processId, moduleProductId);
                await client.UpsertParameterAsync(id, new UpsertParameterRequest(
                    distribution, provenance, parameter.Rationale, null, null, null, covariateId, parameter.CovariateBeta, key), ct);
                continue;
            }

            SimulationFlowKind? flowKind = parameter.FlowKind is null
                ? null
                : ParseHelpers.ParseEnum<SimulationFlowKind>(parameter.FlowKind, "parameter flowKind");

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

    // ---------- Phase 3: channels, integrations, firms, opportunities (historical facts) ----------

    private static async Task<NameIndex> LoadChannelsAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedChannel> channels,
        NameIndex segments, NameIndex products, SeedTally tally, CancellationToken ct)
    {
        var existing = await client.ListChannelsAsync(marketId, null, 1, ListPageSize, ct);
        var index = new NameIndex(existing.Items.Select(c => (c.Name, c.Id)));

        foreach (var channel in channels)
        {
            var id = await GetOrCreateAsync(index, channel.Name, "channel", tally, async () =>
                (await client.CreateChannelAsync(new CreateChannelRequest(
                    marketId, channel.Name, channel.Description, channel.OriginatesOpportunities, channel.CarriesOnlyListedProducts), ct)).Id);

            foreach (var reach in channel.SegmentReaches)
            {
                var segmentId = segments.GetOrThrow(reach.SegmentName);
                await client.SetChannelSegmentReachAsync(id,
                    new SetChannelSegmentReachRequest(segmentId, reach.Reach, reach.OriginationShare), ct);
                tally.RecordApplied("channelSegmentReach");
            }

            foreach (var affinity in channel.ProductAffinities)
            {
                var productId = products.GetOrThrow(affinity.ProductName);
                await client.SetChannelProductAffinityAsync(id, new SetChannelProductAffinityRequest(productId, affinity.Bias), ct);
                tally.RecordApplied("channelProductAffinity");
            }
        }

        return index;
    }

    private static async Task LoadIntegrationsAsync(
        ProductManagerApiClient client, List<SeedIntegration> integrations, NameIndex products, SeedTally tally, CancellationToken ct)
    {
        foreach (var integration in integrations)
        {
            var productAId = products.GetOrThrow(integration.ProductAName);
            var productBId = products.GetOrThrow(integration.ProductBName);

            // No natural-key upsert for integrations — the edge is symmetric, so match either
            // ordering against what a product already carries before creating a duplicate.
            var existing = await client.ListIntegrationsAsync(productAId, 1, ListPageSize, ct);
            var already = existing.Items.Any(i =>
                (i.ProductAId == productAId && i.ProductBId == productBId) ||
                (i.ProductAId == productBId && i.ProductBId == productAId));

            if (already)
            {
                tally.RecordMatched("integration");
                continue;
            }

            await client.CreateIntegrationAsync(
                new CreateIntegrationRequest(productAId, productBId, integration.QualityScore, integration.Notes), ct);
            tally.RecordCreated("integration");
        }
    }

    /// <summary>Delegates to POST /api/firms/bulk — the whole roster in one idempotent call, upserted
    /// by (market, name), relationships included.</summary>
    private static async Task<NameIndex> LoadFirmsAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedFirm> firms,
        NameIndex segments, NameIndex products, NameIndex processes, SeedTally tally, CancellationToken ct)
    {
        if (firms.Count == 0) return new NameIndex();

        var items = firms.Select(f => ToFirmImportItem(f, segments, products, processes)).ToList();
        var response = await client.BulkImportFirmsAsync(new BulkImportFirmsRequest(marketId, items), ct);

        for (var i = 0; i < response.Created; i++) tally.RecordCreated("firm");
        for (var i = 0; i < response.Updated; i++) tally.RecordMatched("firm");
        for (var i = 0; i < response.RelationshipsWritten; i++) tally.RecordApplied("firmRelationship");

        return new NameIndex(response.Firms.Select(f => (f.Name, f.Id)));
    }

    private static FirmImportItem ToFirmImportItem(SeedFirm firm, NameIndex segments, NameIndex products, NameIndex processes)
    {
        Guid? segmentId = firm.SegmentName is null ? null : segments.GetOrThrow(firm.SegmentName);
        var status = ParseHelpers.ParseEnum<EpistemicStatus>(firm.EpistemicStatus, "firm epistemicStatus");
        var relationships = firm.Relationships.Count == 0
            ? null
            : firm.Relationships.Select(r => ToFirmImportRelationship(r, products, processes)).ToList();

        return new FirmImportItem(firm.Name, segmentId, status, firm.AttributesJson, firm.Notes, relationships);
    }

    private static FirmImportRelationship ToFirmImportRelationship(SeedFirmRelationship relationship, NameIndex products, NameIndex processes)
    {
        var productId = products.GetOrThrow(relationship.ProductName);
        var servedProcessIds = relationship.ServedProcessNames.Count == 0
            ? null
            : relationship.ServedProcessNames.Select(processes.GetOrThrow).ToList();

        return new FirmImportRelationship(
            productId, servedProcessIds, relationship.PriceLevel, PricePaidPerPeriod: null,
            ParseHelpers.ParseOptionalDate(relationship.AcquisitionDate, "firm relationship acquisitionDate"));
    }

    /// <summary>
    /// Loads historical, already-decided opportunities through the real open -&gt; qualify -&gt;
    /// decide lifecycle, so the API appends the same OpportunityDecided fact a live decision would —
    /// this is what makes the seed data real calibration input rather than a hand-crafted shortcut.
    /// Idempotency has no natural key to upsert on, so a firm's existing opportunities are matched by
    /// (opened date, scope) before creating — matched ones are left alone rather than re-decided
    /// (a decided opportunity refuses a second decide).
    /// </summary>
    private static async Task LoadOpportunitiesAsync(
        ProductManagerApiClient client, List<SeedOpportunity> opportunities,
        NameIndex firms, NameIndex channels, NameIndex products, NameIndex processes, SeedTally tally, CancellationToken ct)
    {
        var byFirm = new Dictionary<Guid, List<OpportunityDto>>();

        foreach (var seedOpportunity in opportunities)
        {
            var firmId = firms.GetOrThrow(seedOpportunity.FirmName);
            var openedAt = ToUtcMidnight(ParseHelpers.ParseDate(seedOpportunity.OpenedDate, "opportunity openedDate"));
            var scopeIds = seedOpportunity.ScopeProcessNames.Select(processes.GetOrThrow).ToHashSet();

            if (!byFirm.TryGetValue(firmId, out var existingForFirm))
            {
                var existing = await client.ListOpportunitiesAsync(firmId, null, null, null, 1, ListPageSize, ct);
                existingForFirm = existing.Items.ToList();
                byFirm[firmId] = existingForFirm;
            }

            var match = existingForFirm.FirstOrDefault(o =>
                o.OpenedAt.UtcDateTime.Date == openedAt.UtcDateTime.Date &&
                o.Scope.Select(s => s.BusinessProcessId).ToHashSet().SetEquals(scopeIds));

            if (match is not null)
            {
                tally.RecordMatched("opportunity");
                continue;
            }

            Guid? channelId = seedOpportunity.ChannelName is null ? null : channels.GetOrThrow(seedOpportunity.ChannelName);
            Guid? crossSellModuleProductId = seedOpportunity.CrossSellModuleProductName is null
                ? null
                : products.GetOrThrow(seedOpportunity.CrossSellModuleProductName);

            var created = await client.CreateOpportunityAsync(new CreateOpportunityRequest(
                firmId, openedAt, channelId, crossSellModuleProductId, scopeIds.ToList(), CompetitorIds: null, seedOpportunity.Notes), ct);
            tally.RecordCreated("opportunity");
            existingForFirm.Add(created); // keeps duplicate detection correct within this same run

            if (seedOpportunity.QualifiedDate is not null)
            {
                var qualifiedAt = ToUtcMidnight(ParseHelpers.ParseDate(seedOpportunity.QualifiedDate, "opportunity qualifiedDate"));
                await client.QualifyOpportunityAsync(created.Id, new QualifyOpportunityRequest(qualifiedAt), ct);
            }

            var outcome = ParseHelpers.ParseEnum<OpportunityStage>(seedOpportunity.Outcome, "opportunity outcome");
            var decidedAt = ToUtcMidnight(ParseHelpers.ParseDate(seedOpportunity.DecidedDate, "opportunity decidedDate"));
            Guid? winningProductId = seedOpportunity.WinningProductName is null ? null : products.GetOrThrow(seedOpportunity.WinningProductName);
            var recordedAt = ToUtcMidnight(ParseHelpers.ParseOptionalDate(seedOpportunity.RecordedDate, "opportunity recordedDate"));

            await client.DecideOpportunityAsync(created.Id, new DecideOpportunityRequest(outcome, winningProductId, decidedAt, recordedAt), ct);
        }
    }

    private static async Task LoadChurnEventsAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedChurnEvent> churnEvents,
        NameIndex segments, SeedTally tally, CancellationToken ct)
    {
        foreach (var churn in churnEvents)
        {
            var segmentId = segments.GetOrThrow(churn.SegmentName);
            var effectiveDate = ParseHelpers.ParseDate(churn.EffectiveDate, "churn event effectiveDate");

            // The ledger is append-only, so idempotency is the loader's job: skip a (segment, date)
            // pair that already has a churn fact before appending another.
            var existing = await client.ListFactsAsync(
                workspaceId, FactEventKind.Churn, "ClientSegment", segmentId, effectiveDate, effectiveDate, null, 1, ListPageSize, ct);
            if (existing.Items.Count > 0)
            {
                tally.RecordMatched("churnEvent");
                continue;
            }

            var payload = $"{{\"clientSegmentId\":\"{segmentId}\"}}";
            var recordedAt = ToUtcMidnight(ParseHelpers.ParseOptionalDate(churn.RecordedDate, "churn event recordedDate"));
            await client.CreateFactAsync(new CreateFactEventRequest(
                workspaceId, FactEventKind.Churn, effectiveDate, "ClientSegment", segmentId, payload, churn.Notes, recordedAt), ct);
            tally.RecordCreated("churnEvent");
        }
    }

    private static async Task LoadArrivalCountsAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedArrivalCount> arrivals,
        NameIndex segments, SeedTally tally, CancellationToken ct)
    {
        foreach (var arrival in arrivals)
        {
            var segmentId = segments.GetOrThrow(arrival.SegmentName);
            var flowKind = ParseHelpers.ParseEnum<SimulationFlowKind>(arrival.FlowKind, "arrival count flowKind");
            var month = ParseHelpers.ParseDate(arrival.Month, "arrival count month");
            var monthStart = new DateOnly(month.Year, month.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var existing = await client.ListFactsAsync(workspaceId, FactEventKind.SegmentArrivalObserved,
                "ClientSegment", segmentId, monthStart, monthEnd, null, 1, ListPageSize, ct);
            if (existing.Items.Any(f => PayloadFlowKindEquals(f.PayloadJson, flowKind)))
            {
                tally.RecordMatched("arrivalCount");
                continue;
            }

            var payload = $"{{\"clientSegmentId\":\"{segmentId}\",\"flowKind\":\"{flowKind}\",\"count\":{arrival.Count}," +
                          $"\"exposure\":{arrival.Exposure.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
            var recordedAt = ToUtcMidnight(ParseHelpers.ParseOptionalDate(arrival.RecordedDate, "arrival count recordedDate"));
            await client.CreateFactAsync(new CreateFactEventRequest(
                workspaceId, FactEventKind.SegmentArrivalObserved, monthStart, "ClientSegment", segmentId, payload, null, recordedAt), ct);
            tally.RecordCreated("arrivalCount");
        }
    }

    /// <summary>
    /// Reads <c>flowKind</c> out of a stored arrival fact's payload. This must parse rather than
    /// substring-match: the payload column is jsonb, and Postgres re-serializes it with a space after
    /// each colon (<c>{"flowKind": "NewFirmArrival"}</c>), so a match against the compact text the
    /// loader writes never fires and every re-run appends a duplicate fact.
    /// </summary>
    private static bool PayloadFlowKindEquals(string? payloadJson, SimulationFlowKind flowKind)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("flowKind", out var element)) return false;
            if (element.ValueKind != System.Text.Json.JsonValueKind.String) return false;
            return string.Equals(element.GetString(), flowKind.ToString(), StringComparison.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static DateTimeOffset ToUtcMidnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset? ToUtcMidnight(DateOnly? date) => date is null ? null : ToUtcMidnight(date.Value);

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
