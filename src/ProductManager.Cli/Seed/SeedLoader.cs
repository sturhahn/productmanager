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
/// Rows with source identity (PRD6: firms, relationships, opportunities, churn, arrivals) match by
/// their source binding instead — see <c>SeedLoader.Sources.cs</c>.
///
/// <para>
/// Every load is two passes over the same code. <see cref="PlanAsync"/> reads the API, decides every
/// record and writes nothing (placeholder ids stand in for what it would create); problems that make
/// the file unsafe are collected, all of them, instead of stopping at the first. Only a clean plan is
/// applied (<see cref="ApplyAsync"/>), in the fixed dependency order below, recording each record's
/// actual outcome. Records are written one by one — there is no transaction across records or
/// datasets — but every write is repeat-safe, so a pass that stops midway resumes by running the same
/// file again.
/// </para>
///
/// References resolve against the whole workspace, not just the file being loaded: a file may name
/// a segment, product, process, approach or project that an earlier file already put in the API
/// (that is how phase2/phase3 build on phase1's market). See <see cref="MarketResolver"/>,
/// <see cref="ProjectResolver"/> and <see cref="FirmResolver"/> for the lazy fallbacks that make that work.
/// </summary>
public static partial class SeedLoader
{
    private const int ListPageSize = 200; // the API's maximum page size; every listing is paged to the end.

    /// <summary>The dry-run pass: every read, every decision, no write. Blocking problems land in
    /// <see cref="SeedTally.Problems"/>; planned actions in <see cref="SeedTally.Records"/>.</summary>
    public static async Task<SeedTally> PlanAsync(ProductManagerApiClient client, SeedFile seed, CancellationToken ct)
    {
        var tally = new SeedTally(dryRun: true);
        foreach (var problem in SeedPreflight.Check(seed)) tally.Block(problem);
        await LoadAsync(client, seed, tally, ct);
        return tally;
    }

    /// <summary>Applies the file, recording actual outcomes into <paramref name="tally"/>. Call only
    /// after a <see cref="PlanAsync"/> with no problems. A write the API refuses (other than a
    /// correction-required 409, which becomes that record's exception) stops the pass by throwing.</summary>
    public static Task ApplyAsync(ProductManagerApiClient client, SeedFile seed, SeedTally tally, CancellationToken ct) =>
        LoadAsync(client, seed, tally, ct);

    /// <summary>Plan, then apply. Throws <see cref="SeedReferenceException"/> listing every problem,
    /// having written nothing, when the plan finds the file unsafe.</summary>
    public static async Task<SeedTally> RunAsync(ProductManagerApiClient client, SeedFile seed, CancellationToken ct)
    {
        var plan = await PlanAsync(client, seed, ct);
        if (plan.Problems.Count > 0) throw SeedReferenceException.WithMessage(DescribeProblems(plan.Problems));

        var tally = new SeedTally();
        await ApplyAsync(client, seed, tally, ct);
        return tally;
    }

    public static string DescribeProblems(IReadOnlyList<string> problems) =>
        $"the seed file has {problems.Count} problem(s); nothing was written:" +
        string.Concat(problems.Select(p => $"{Environment.NewLine}  - {p}"));

    private static async Task LoadAsync(ProductManagerApiClient client, SeedFile seed, SeedTally tally, CancellationToken ct)
    {
        var workspaceId = await LoadWorkspaceAsync(client, seed.Workspace, tally, ct);

        MarketIndexes? market = null;
        if (seed.Market is not null)
            await Guard(tally, "market", seed.Market.Name, async () =>
                market = await LoadMarketAsync(client, workspaceId, seed.Market, tally, ct));

        if (seed.MarketDynamics is not null)
        {
            // A combined file's dynamics attach to the market it just loaded; reusing those indexes is
            // what lets a dry run resolve names that exist only as planned creates.
            var sameMarket = market is not null && SameName(seed.Market!.Name, seed.MarketDynamics.MarketName) ? market : null;
            await Guard(tally, "marketDynamics", seed.MarketDynamics.MarketName, () =>
                LoadMarketDynamicsAsync(client, workspaceId, seed.MarketDynamics, sameMarket, tally, ct));
        }

        var marketResolver = new MarketResolver(client, workspaceId, market);
        var projectResolver = new ProjectResolver(client, workspaceId);

        var teamIndex = await LoadTeamsAsync(client, workspaceId, seed.Teams, tally, ct);
        var initiativeIndex = await LoadInitiativesAsync(client, workspaceId, seed.Initiatives, marketResolver, tally, ct);
        var featureIndex = await LoadFeaturesAsync(client, workspaceId, seed.Features, marketResolver, tally, ct);
        await LoadProjectsAsync(client, seed.Projects, projectResolver, initiativeIndex, teamIndex, featureIndex, tally, ct);

        if (seed.Roadmap is not null)
            await Guard(tally, "roadmap", seed.Roadmap.Name, () => LoadRoadmapAsync(client, workspaceId, seed.Roadmap, projectResolver, tally, ct));
    }

    /// <summary>
    /// Runs one record. A broken reference or unparseable value is, in a dry run, a blocking problem
    /// (collected, so every one is listed); while applying, a broken reference can only come from
    /// something the API refused earlier in this pass (a firm exception), so it becomes this record's
    /// exception and the pass continues. Everything else propagates.
    /// </summary>
    private static async Task Guard(SeedTally tally, string kind, string? key, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex) when (ex is SeedReferenceException || (ex is ArgumentException && tally.DryRun))
        {
            if (tally.DryRun) tally.Block($"{(key is null ? kind : $"{kind} {key}")}: {ex.Message}");
            else tally.Record(kind, key, SeedAction.Exception, SeedAction.Exception, ex.Message);
        }
    }

    /// <summary>Performs a write unless this is a dry run.</summary>
    private static Task WriteAsync(SeedTally tally, Func<Task> write) => tally.DryRun ? Task.CompletedTask : write();

    // ---------- Workspace ----------

    private static async Task<Guid> LoadWorkspaceAsync(ProductManagerApiClient client, SeedWorkspace? seed, SeedTally tally, CancellationToken ct)
    {
        if (seed is null || string.IsNullOrWhiteSpace(seed.Name))
        {
            var defaultWorkspace = await client.GetDefaultWorkspaceAsync(ct);
            tally.RecordMatched("workspace", defaultWorkspace.Name);
            return defaultWorkspace.Id;
        }

        var existing = await ListAllAsync(page => client.ListWorkspacesAsync(page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(w => (w.Name, w.Id)));
        if (index.TryGet(seed.Name, out var id))
        {
            tally.RecordMatched("workspace", seed.Name);
            return id;
        }

        // Phase 1 is single-workspace: the API bootstraps one workspace on first run and the
        // frontend always resolves it through GET /api/workspaces/default (the oldest one).
        // Creating a second workspace here would load the whole seed into a workspace the UI
        // never shows, so adopt the bootstrap workspace and give it the seed's name instead.
        var target = await client.GetDefaultWorkspaceAsync(ct);
        await WriteAsync(tally, () => client.UpdateWorkspaceAsync(target.Id, new UpdateWorkspaceRequest(seed.Name), ct));
        tally.RecordCreated("workspace", seed.Name);
        return target.Id;
    }

    // ---------- Teams ----------

    private static async Task<NameIndex> LoadTeamsAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedTeam> teams, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListAllAsync(page => client.ListTeamsAsync(workspaceId, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(t => (t.Name, t.Id)));

        foreach (var team in teams)
            await Guard(tally, "team", team.Name, async () =>
            {
                var id = await GetOrCreateAsync(index, team.Name, "team", tally,
                    async () => (await client.CreateTeamAsync(new CreateTeamRequest(workspaceId, team.Name, team.SpecializationTag), ct)).Id);

                foreach (var capacity in team.Capacities)
                    await WriteAsync(tally, () => client.SetTeamCapacityAsync(id, new SetTeamCapacityRequest(capacity.Year, capacity.Month, capacity.Points), ct));
            });

        return index;
    }

    // ---------- Initiatives ----------

    private static async Task<NameIndex> LoadInitiativesAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedInitiative> initiatives,
        MarketResolver marketResolver, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListAllAsync(page => client.ListInitiativesAsync(workspaceId, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(i => (i.Name, i.Id)));

        foreach (var initiative in initiatives)
            await Guard(tally, "initiative", initiative.Name, async () =>
            {
                // Only a file that actually targets segments needs a market resolved for it.
                var segmentIndex = initiative.TargetSegmentNames.Count == 0
                    ? null
                    : (await marketResolver.TryResolveAsync(ct))?.Segments;

                var segmentIds = initiative.TargetSegmentNames.Count == 0
                    ? null
                    : initiative.TargetSegmentNames.Select(n => ResolveSegment(segmentIndex, n)).ToList();

                var strategicIntent = ParseHelpers.ParseOptionalEnum<StrategicIntent>(initiative.StrategicIntent, "initiative strategicIntent");

                await GetOrCreateAsync(index, initiative.Name, "initiative", tally, async () =>
                    (await client.CreateInitiativeAsync(
                        new CreateInitiativeRequest(workspaceId, initiative.Name, initiative.Outcome, initiative.HypothesizedValue,
                            segmentIds, TargetNeedIds: null, strategicIntent), ct)).Id);
            });

        return index;
    }

    private static Guid ResolveSegment(NameIndex? segmentIndex, string name) =>
        segmentIndex is null
            ? throw new SeedReferenceException($"segment '{name}' (no market/segments defined in the seed file)")
            : segmentIndex.GetOrThrow(name);

    // ---------- Features ----------

    private static async Task<NameIndex> LoadFeaturesAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedFeature> features, MarketResolver marketResolver,
        SeedTally tally, CancellationToken ct)
    {
        var existing = await ListAllAsync(page => client.ListFeaturesAsync(workspaceId, null, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(f => (f.Name, f.Id)));

        foreach (var feature in features)
            await Guard(tally, "feature", feature.Name, async () =>
            {
                // Resolved once per feature and only when something in it actually names a market
                // entity — a purely execution-side features file never lists markets at all.
                var market = feature.ProductName is not null || feature.Deltas.Any(DeltaNeedsMarket)
                    ? await marketResolver.TryResolveAsync(ct)
                    : null;

                Guid? productId = feature.ProductName is null
                    ? null
                    : RequireMarket(market).Products.GetOrThrow(feature.ProductName);

                var id = await GetOrCreateAsync(index, feature.Name, "feature", tally, async () =>
                    (await client.CreateFeatureAsync(new CreateFeatureRequest(workspaceId, feature.Name, feature.Description, productId), ct)).Id);

                if (feature.Status is not null)
                {
                    var status = ParseHelpers.ParseEnum<FeatureStatus>(feature.Status, "feature status");
                    var shippedDate = ParseHelpers.ParseOptionalDate(feature.ShippedDate, "feature shippedDate");
                    await WriteAsync(tally, () => client.UpdateFeatureAsync(id,
                        new UpdateFeatureRequest(feature.Name, feature.Description, productId, status, shippedDate), ct));
                }

                if (feature.Deltas.Count > 0)
                {
                    var deltaRequests = feature.Deltas.Select(d => ToDeltaRequest(d, market)).ToList();
                    // Replace-all is idempotent regardless of what the feature already carries.
                    await WriteAsync(tally, () => client.ReplaceFeatureDeltasAsync(id, deltaRequests, ct));
                }
            });

        return index;
    }

    /// <summary>Whether a delta names anything that has to be looked up in a market's indexes.</summary>
    private static bool DeltaNeedsMarket(SeedFeatureDelta delta) =>
        delta.BusinessProcessName is not null || delta.ApproachName is not null || delta.SegmentName is not null;

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

    private static async Task LoadProjectsAsync(
        ProductManagerApiClient client, List<SeedProject> projects, ProjectResolver resolver,
        NameIndex initiativeIndex, NameIndex teamIndex, NameIndex featureIndex, SeedTally tally, CancellationToken ct)
    {
        var index = resolver.Index;
        // Projects are scoped to an initiative in the API, so list per-initiative once each,
        // rather than a workspace-wide listing keyed only by name.
        var listedInitiatives = new HashSet<Guid>();

        foreach (var project in projects)
            await Guard(tally, "project", project.Name, async () =>
            {
                var initiativeId = initiativeIndex.GetOrThrow(project.InitiativeName);

                if (listedInitiatives.Add(initiativeId))
                    foreach (var p in await ListChildrenAsync(tally, initiativeId,
                                 page => client.ListProjectsAsync(null, initiativeId, page, ListPageSize, ct)))
                        index.Add(p.Name, p.Id);

                var status = ParseHelpers.ParseOptionalEnum<ProjectStatus>(project.Status, "project status");
                var startDate = ParseHelpers.ParseOptionalDate(project.StartDate, "project startDate");
                var endDate = ParseHelpers.ParseOptionalDate(project.EndDate, "project endDate");

                await GetOrCreateAsync(index, project.Name, "project", tally, async () =>
                    (await client.CreateProjectAsync(
                        new CreateProjectRequest(initiativeId, project.Name, project.Description, status, startDate, endDate), ct)).Id);
            });

        // Dependencies and delivered features reference other projects/features, so resolve them
        // in a second pass once every project in this file has an id.
        foreach (var project in projects)
            await Guard(tally, "project", project.Name, async () =>
            {
                if (!index.TryGet(project.Name, out var id)) return; // its first pass already reported why

                foreach (var dependsOnName in project.DependsOnProjectNames)
                {
                    var dependsOnId = await resolver.ResolveAsync(dependsOnName, ct);
                    await WriteAsync(tally, () => client.AddProjectDependencyAsync(id, dependsOnId, ct));
                }

                foreach (var demand in project.EffortDemands)
                {
                    var teamId = teamIndex.GetOrThrow(demand.TeamName);
                    await WriteAsync(tally, () => client.SetEffortDemandAsync(id,
                        new SetEffortDemandRequest(teamId, demand.Year, demand.Month, demand.Points), ct));
                }

                foreach (var featureName in project.DeliversFeatureNames)
                {
                    var featureId = featureIndex.GetOrThrow(featureName);
                    await WriteAsync(tally, () => client.LinkFeatureAsync(id, featureId, ct));
                }
            });
    }

    // ---------- Roadmap ----------

    private static async Task LoadRoadmapAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedRoadmap roadmap, ProjectResolver projects, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListAllAsync(page => client.ListRoadmapsAsync(workspaceId, null, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(r => (r.Name, r.Id)));

        var id = await GetOrCreateAsync(index, roadmap.Name, "roadmap", tally, async () =>
            (await client.CreateRoadmapAsync(new CreateRoadmapRequest(workspaceId, roadmap.Name, roadmap.VersionName, IsLive: false), ct)).Id);

        foreach (var item in roadmap.Items)
            await Guard(tally, "roadmapItem", item.ProjectName, async () =>
            {
                var projectId = await projects.ResolveAsync(item.ProjectName, ct);
                var start = ParseHelpers.ParseDate(item.Start, "roadmap item start");
                var end = ParseHelpers.ParseDate(item.End, "roadmap item end");
                // PlanProject is upsert (re-plans if already present), so this is safe to repeat.
                await WriteAsync(tally, () => client.PlanProjectAsync(id, new PlanProjectRequest(projectId, start, end, item.SortOrder), ct));
            });

        if (roadmap.IsLive)
            await WriteAsync(tally, () => client.MakeRoadmapLiveAsync(id, ct));
    }

    // ---------- Market ----------

    private static async Task<MarketIndexes> LoadMarketAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedMarket seed, SeedTally tally, CancellationToken ct)
    {
        var existingMarkets = await ListAllAsync(page => client.ListMarketsAsync(workspaceId, page, ListPageSize, ct));
        var marketIndex = new NameIndex(existingMarkets.Select(m => (m.Name, m.Id)));
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
        var existing = await ListChildrenAsync(tally, marketId, page => client.ListClientSegmentsAsync(marketId, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(s => (s.Name, s.Id)));

        foreach (var segment in segments)
            await GetOrCreateAsync(index, segment.Name, "clientSegment", tally, async () =>
                (await client.CreateClientSegmentAsync(
                    new CreateClientSegmentRequest(marketId, segment.Name, segment.Description, segment.SuiteAffinity, segment.ConsolidationPreference), ct)).Id);

        return index;
    }

    private static async Task<NameIndex> LoadBusinessProcessesAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedBusinessProcess> processes, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListChildrenAsync(tally, marketId, page => client.ListBusinessProcessesAsync(marketId, null, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(p => (p.Name, p.Id)));

        // Parents must appear before their children in the seed file — the natural authoring order.
        foreach (var process in processes)
            await Guard(tally, "businessProcess", process.Name, async () =>
            {
                Guid? parentId = process.ParentProcessName is null ? null : index.GetOrThrow(process.ParentProcessName);
                await GetOrCreateAsync(index, process.Name, "businessProcess", tally, async () =>
                    (await client.CreateBusinessProcessAsync(
                        new CreateBusinessProcessRequest(marketId, process.Name, process.Description, parentId), ct)).Id);
            });

        return index;
    }

    private static async Task<NameIndex> LoadApproachesAsync(
        ProductManagerApiClient client, NameIndex businessProcesses, List<SeedApproach> approaches, SeedTally tally, CancellationToken ct)
    {
        var index = new NameIndex();
        var listedProcesses = new HashSet<Guid>();

        foreach (var approach in approaches)
            await Guard(tally, "approach", approach.Name, async () =>
            {
                var businessProcessId = businessProcesses.GetOrThrow(approach.BusinessProcessName);
                if (listedProcesses.Add(businessProcessId))
                    foreach (var a in await ListChildrenAsync(tally, businessProcessId,
                                 page => client.ListApproachesAsync(businessProcessId, page, ListPageSize, ct)))
                        index.Add(ApproachKey(approach.BusinessProcessName, a.Name), a.Id);

                var key = ApproachKey(approach.BusinessProcessName, approach.Name);
                await GetOrCreateAsync(index, key, "approach", tally, async () =>
                    (await client.CreateApproachAsync(new CreateApproachRequest(businessProcessId, approach.Name, approach.Description), ct)).Id);
            });

        // A file may reference an approach (in a coverage row or a feature delta) under a process it
        // does not itself list approaches for — including a file with no `approaches` at all. Cover
        // every process in the index, not just the ones this file's approaches mention.
        await ListRemainingApproachesAsync(client, businessProcesses, index, listedProcesses, tally, ct);

        return index;
    }

    /// <summary>Fills <paramref name="index"/> with the API's approaches for every process in
    /// <paramref name="businessProcesses"/> that has not been listed yet, keyed
    /// <c>process::approach</c>. Existing entries win — a freshly created approach keeps its id.</summary>
    private static async Task ListRemainingApproachesAsync(
        ProductManagerApiClient client, NameIndex businessProcesses, NameIndex index, HashSet<Guid> listedProcesses,
        SeedTally tally, CancellationToken ct)
    {
        foreach (var (processName, processId) in businessProcesses.Entries)
        {
            if (!listedProcesses.Add(processId)) continue;

            foreach (var approach in await ListChildrenAsync(tally, processId, page => client.ListApproachesAsync(processId, page, ListPageSize, ct)))
            {
                var key = ApproachKey(processName, approach.Name);
                if (!index.TryGet(key, out _))
                    index.Add(key, approach.Id);
            }
        }
    }

    private static string ApproachKey(string businessProcessName, string approachName) =>
        $"{businessProcessName.Trim()}::{approachName.Trim()}";

    private static async Task<NameIndex> LoadCompetitorsAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedCompetitor> competitors, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListChildrenAsync(tally, marketId, page => client.ListCompetitorsAsync(marketId, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(c => (c.Name, c.Id)));

        foreach (var competitor in competitors)
            await GetOrCreateAsync(index, competitor.Name, "competitor", tally, async () =>
                (await client.CreateCompetitorAsync(new CreateCompetitorRequest(marketId, competitor.Name, competitor.StrategyNotes), ct)).Id);

        return index;
    }

    private static async Task<NameIndex> LoadProductsAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedProduct> products,
        NameIndex competitors, NameIndex segments, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListChildrenAsync(tally, marketId, page => client.ListProductsAsync(marketId, null, null, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(p => (p.Name, p.Id)));

        // Parent products (for modules) must appear before their modules in the seed file.
        foreach (var product in products)
            await Guard(tally, "product", product.Name, async () =>
            {
                Guid? competitorId = product.CompetitorName is null ? null : competitors.GetOrThrow(product.CompetitorName);
                Guid? parentProductId = product.ParentProductName is null ? null : index.GetOrThrow(product.ParentProductName);

                var id = await GetOrCreateAsync(index, product.Name, "product", tally, async () =>
                    (await client.CreateProductAsync(
                        new CreateProductRequest(marketId, product.Name, product.Description, competitorId, parentProductId), ct)).Id);

                foreach (var price in product.Prices)
                {
                    Guid? segmentId = price.SegmentName is null ? null : segments.GetOrThrow(price.SegmentName);
                    await WriteAsync(tally, () => client.SetProductPriceAsync(id, new SetProductPriceRequest(
                        new MoneyDto(price.RecurringAmount, price.Currency),
                        new MoneyDto(price.ImplementationFeeAmount, price.ImplementationFeeCurrency ?? price.Currency),
                        segmentId,
                        price.UsageBasedPlaceholder), ct));
                }
            });

        return index;
    }

    private static async Task LoadCoveragesAsync(
        ProductManagerApiClient client, List<SeedCoverage> coverages,
        NameIndex products, NameIndex businessProcesses, NameIndex approaches, NameIndex segments,
        SeedTally tally, CancellationToken ct)
    {
        foreach (var coverage in coverages)
        {
            var key = $"{coverage.ProductName} / {coverage.BusinessProcessName}" +
                      (coverage.SegmentName is null ? "" : $" / {coverage.SegmentName}") +
                      (coverage.ApproachName is null ? "" : $" / {coverage.ApproachName}");
            await Guard(tally, "coverage", key, async () =>
            {
                var productId = products.GetOrThrow(coverage.ProductName);
                var businessProcessId = businessProcesses.GetOrThrow(coverage.BusinessProcessName);
                Guid? approachId = coverage.ApproachName is null
                    ? null
                    : approaches.GetOrThrow(ApproachKey(coverage.BusinessProcessName, coverage.ApproachName));
                Guid? segmentId = coverage.SegmentName is null ? null : segments.GetOrThrow(coverage.SegmentName);
                var tier = ParseHelpers.ParseEnum<CoverageTier>(coverage.Tier, "coverage tier");
                var effectiveDate = ParseHelpers.ParseOptionalDate(coverage.EffectiveDate, "coverage effectiveDate");

                // No upsert endpoint for coverage — match the existing assessment for this
                // (product, process, segment) by hand before creating a duplicate.
                var planned = tally.IsPlaceholder(productId) || tally.IsPlaceholder(businessProcessId) || tally.IsPlaceholder(segmentId);
                var existing = planned
                    ? []
                    : await ListAllAsync(page => client.ListCoveragesAsync(productId, businessProcessId, segmentId, page, ListPageSize, ct));
                if (existing.Any(c => c.ApproachId == approachId))
                {
                    tally.RecordMatched("coverage", key);
                    return;
                }

                await WriteAsync(tally, () => client.CreateCoverageAsync(new CreateCoverageRequest(
                    productId, businessProcessId, tier, coverage.IsUnknown, coverage.Confidence,
                    approachId, segmentId, effectiveDate, coverage.Rationale), ct));
                tally.RecordCreated("coverage", key);
            });
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
            var key = $"{need.SegmentName} / {need.BusinessProcessName}";
            await Guard(tally, "need", key, async () =>
            {
                var segmentId = segments.GetOrThrow(need.SegmentName);
                var businessProcessId = businessProcesses.GetOrThrow(need.BusinessProcessName);

                if (need.RequiredCoverage is < 0 or > 1)
                    throw new ArgumentException(
                        $"need '{need.SegmentName}' / '{need.BusinessProcessName}' has requiredCoverage " +
                        $"{need.RequiredCoverage} — it must be between 0 and 1.");

                var planned = tally.IsPlaceholder(segmentId) || tally.IsPlaceholder(businessProcessId);
                if (!planned && (await client.ListNeedsAsync(null, segmentId, businessProcessId, 1, 1, ct)).Items.Count > 0)
                {
                    tally.RecordMatched("need", key);
                    return;
                }

                // The coverage floor rides in the need's requiredDepthJson side-channel, which is where
                // FirmLevelInputBuilder reads it from (industry specifics as data, not schema).
                string? requiredDepthJson = need.RequiredCoverage is null
                    ? null
                    : $"{{\"requiredCoverage\":{need.RequiredCoverage.Value.ToString(CultureInfo.InvariantCulture)}}}";

                await WriteAsync(tally, () => client.CreateNeedAsync(new CreateNeedRequest(
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
                    Notes: need.Notes), ct));
                tally.RecordCreated("need", key);
            });
        }
    }

    // ---------- Market dynamics (Phase 2) ----------

    private static async Task LoadMarketDynamicsAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedMarketDynamics dynamics, MarketIndexes? fileMarket,
        SeedTally tally, CancellationToken ct)
    {
        // Every index here comes from the API's listing for this market (or from the market this same
        // file just loaded, which is that listing plus what it created), never from the file's own
        // entries, so a fragment that only carries (say) opportunities still resolves segment,
        // product, process and channel names an earlier file created.
        var market = fileMarket;
        if (market is null)
        {
            var markets = new NameIndex((await ListAllAsync(page => client.ListMarketsAsync(workspaceId, page, ListPageSize, ct)))
                .Select(m => (m.Name, m.Id)));
            market = await BuildMarketIndexesAsync(client, markets.GetOrThrow(dynamics.MarketName), tally, ct);
        }

        foreach (var stockState in dynamics.SegmentStockStates)
            await Guard(tally, "segmentStock", stockState.SegmentName, () =>
                ApplySegmentStockStateAsync(client, market.Segments, market.Products, stockState, tally, ct));

        var covariateIndex = await LoadCovariatesAsync(client, workspaceId, dynamics.Covariates, tally, ct);

        foreach (var assumptionSet in dynamics.AssumptionSets)
            await Guard(tally, "assumptionSet", assumptionSet.Name, () =>
                LoadAssumptionSetAsync(client, workspaceId, assumptionSet, market.Segments, market.Products, market.BusinessProcesses,
                    covariateIndex, tally, ct));

        foreach (var marketEvent in dynamics.MarketEvents)
            await Guard(tally, "marketEvent", marketEvent.Name, () =>
                LoadMarketEventAsync(client, market.MarketId, marketEvent, market.Segments, market.Competitors, tally, ct));

        // ---------- Phase 3 ----------

        var channelIndex = await LoadChannelsAsync(client, market.MarketId, dynamics.Channels, market.Segments, market.Products, tally, ct);
        await LoadIntegrationsAsync(client, dynamics.Integrations, market.Products, tally, ct);

        var firms = new FirmResolver(client, workspaceId, market.MarketId, tally);
        var defaults = dynamics.SourceDefaults;
        await LoadFirmsAsync(client, workspaceId, market, dynamics.Firms, defaults, firms, tally, ct);
        await LoadOpportunitiesAsync(client, workspaceId, dynamics.Opportunities, defaults, firms, channelIndex, market, tally, ct);
        await LoadChurnEventsAsync(client, workspaceId, dynamics.ChurnEvents, defaults, market, firms, tally, ct);
        await LoadArrivalCountsAsync(client, workspaceId, dynamics.ArrivalCounts, defaults, market.Segments, tally, ct);
    }

    private static async Task ApplySegmentStockStateAsync(
        ProductManagerApiClient client, NameIndex segments, NameIndex products, SeedSegmentStockState stockState,
        SeedTally tally, CancellationToken ct)
    {
        var segmentId = segments.GetOrThrow(stockState.SegmentName);

        // SetStockState replaces the whole measurement, so re-applying the same seed value is a no-op
        // — safe to run on every load, the same way team capacities are simply re-applied every run.
        // There is no create-vs-match decision here, so these count as "applied" in the tally.
        await WriteAsync(tally, () => client.SetSegmentStockStateAsync(segmentId, new SetSegmentStockStateRequest(
            stockState.InitialProspectPool, stockState.InitialClientCount, stockState.ArpuPerMonth,
            stockState.ImplementationFee, stockState.ServicingCostPerClient, stockState.InfraFixedCostPerMonth,
            stockState.InfraCostPerClient, stockState.RdDragPerMonth), ct));
        tally.RecordApplied("segmentStock", stockState.SegmentName);

        foreach (var penetration in stockState.ModulePenetrations)
        {
            var moduleProductId = products.GetOrThrow(penetration.ModuleProductName);
            await WriteAsync(tally, () => client.SetSegmentModulePenetrationAsync(segmentId,
                new SetSegmentModulePenetrationRequest(moduleProductId, penetration.Penetration, penetration.ArpuPerMonth), ct));
            tally.RecordApplied("modulePenetration", $"{stockState.SegmentName} / {penetration.ModuleProductName}");
        }
    }

    private static async Task<NameIndex> LoadCovariatesAsync(
        ProductManagerApiClient client, Guid workspaceId, List<SeedCovariate> covariates, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListAllAsync(page => client.ListCovariatesAsync(workspaceId, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(c => (c.Name, c.Id)));

        foreach (var covariate in covariates)
            await Guard(tally, "covariate", covariate.Name, async () =>
            {
                var id = await GetOrCreateAsync(index, covariate.Name, "covariate", tally, async () =>
                    (await client.CreateCovariateAsync(new CreateCovariateRequest(
                        workspaceId, covariate.Name, covariate.Description, covariate.Unit,
                        covariate.GbmStart, covariate.GbmDrift, covariate.GbmVol), ct)).Id);

                foreach (var observation in covariate.Observations)
                    await Guard(tally, "covariateObservation", $"{covariate.Name} {observation.EffectiveDate}", () =>
                        ApplyCovariateObservationAsync(client, workspaceId, id, covariate.Name, observation, tally, ct));
            });

        return index;
    }

    private static async Task ApplyCovariateObservationAsync(
        ProductManagerApiClient client, Guid workspaceId, Guid covariateId, string covariateName, SeedCovariateObservation observation,
        SeedTally tally, CancellationToken ct)
    {
        var key = $"{covariateName} {observation.EffectiveDate}";
        var effectiveDate = ParseHelpers.ParseDate(observation.EffectiveDate, "covariate observation effectiveDate");
        var recordedAt = ToUtcMidnight(ParseHelpers.ParseOptionalDate(observation.RecordedDate, "covariate observation recordedDate"));

        // The facts ledger is append-only (no natural-key upsert on the API side), so idempotency is
        // the seed loader's job here: skip a date that already has a reading before appending another.
        if (!tally.IsPlaceholder(covariateId) &&
            (await client.GetCovariateHistoryAsync(covariateId, effectiveDate, effectiveDate, null, ct)).Observations.Count > 0)
        {
            tally.RecordMatched("covariateObservation", key);
            return;
        }

        var payload = $"{{\"value\":{observation.Value.ToString(CultureInfo.InvariantCulture)}}}";
        await WriteAsync(tally, () => client.CreateFactAsync(new CreateFactEventRequest(
            workspaceId, FactEventKind.CovariateObserved, effectiveDate,
            SubjectType: "Covariate", SubjectId: covariateId, PayloadJson: payload, Notes: observation.Notes,
            RecordedAt: recordedAt), ct));
        tally.RecordCreated("covariateObservation", key);
    }

    private static async Task LoadAssumptionSetAsync(
        ProductManagerApiClient client, Guid workspaceId, SeedAssumptionSet assumptionSet,
        NameIndex segments, NameIndex products, NameIndex processes, NameIndex covariates, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListAllAsync(page => client.ListAssumptionSetsAsync(workspaceId, null, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(a => (a.Name, a.Id)));

        var id = await GetOrCreateAsync(index, assumptionSet.Name, "assumptionSet", tally, async () =>
            (await client.CreateAssumptionSetAsync(new CreateAssumptionSetRequest(workspaceId, assumptionSet.Name, assumptionSet.Notes), ct)).Id);

        // UpsertParameter is upsert-by-engine-key (or by free-form Key) on the API side, so
        // re-applying every parameter on every load is safe — same idempotency story as capacities.
        foreach (var parameter in assumptionSet.Parameters)
        {
            var key = $"{assumptionSet.Name}: {parameter.Key ?? parameter.ChoiceKind ?? $"{parameter.SegmentName} {parameter.FlowKind}"}";
            await Guard(tally, "parameter", key, async () =>
            {
                Guid? segmentId = parameter.SegmentName is null ? null : segments.GetOrThrow(parameter.SegmentName);
                Guid? moduleProductId = parameter.ModuleProductName is null ? null : products.GetOrThrow(parameter.ModuleProductName);
                Guid? covariateId = parameter.CovariateName is null ? null : covariates.GetOrThrow(parameter.CovariateName);

                var distribution = new DistributionSpecDto(
                    ParseHelpers.ParseEnum<DistributionType>(parameter.DistType, "parameter distType"), parameter.P1, parameter.P2);
                var provenance = ParseHelpers.ParseEnum<ParameterProvenance>(parameter.Provenance, "parameter provenance");
                var evidence = parameter.PriorEvidence is not { } e ? null : new PriorEvidenceDto(
                    ParseHelpers.ParseEnum<PriorEvidenceBasis>(e.Basis, "parameter priorEvidence basis"),
                    ParseHelpers.ParseOptionalDate(e.ObservedFrom, "parameter priorEvidence observedFrom"),
                    ParseHelpers.ParseOptionalDate(e.ObservedThrough, "parameter priorEvidence observedThrough"),
                    ParseHelpers.ParseOptionalDate(e.RecordedThrough, "parameter priorEvidence recordedThrough"),
                    e.Population);

                if (parameter.ChoiceKind is not null)
                {
                    // A choice-model parameter: keyed via ChoiceParameterKeys, always free-form on the
                    // wire (segmentId/flowKind stay null so the API treats it as a Key parameter, not an
                    // engine one — see ParameterKeyHelper.Validate).
                    Guid? processId = parameter.BusinessProcessName is null ? null : processes.GetOrThrow(parameter.BusinessProcessName);
                    var choiceKey = ChoiceParameterKeys.Build(parameter.ChoiceKind, segmentId, processId, moduleProductId);
                    await WriteAsync(tally, () => client.UpsertParameterAsync(id, new UpsertParameterRequest(
                        distribution, provenance, parameter.Rationale, null, null, null, covariateId, parameter.CovariateBeta, choiceKey,
                        evidence), ct));
                    return;
                }

                SimulationFlowKind? flowKind = parameter.FlowKind is null
                    ? null
                    : ParseHelpers.ParseEnum<SimulationFlowKind>(parameter.FlowKind, "parameter flowKind");

                await WriteAsync(tally, () => client.UpsertParameterAsync(id, new UpsertParameterRequest(
                    distribution, provenance, parameter.Rationale, segmentId, flowKind, moduleProductId,
                    covariateId, parameter.CovariateBeta, parameter.Key, evidence), ct));
            });
        }
    }

    private static async Task LoadMarketEventAsync(
        ProductManagerApiClient client, Guid marketId, SeedMarketEvent marketEvent,
        NameIndex segments, NameIndex competitors, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListChildrenAsync(tally, marketId, page => client.ListMarketEventsAsync(marketId, null, null, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(e => (e.Name, e.Id)));

        var competitorId = marketEvent.CompetitorName is null ? (Guid?)null : competitors.GetOrThrow(marketEvent.CompetitorName);
        var rateMultipliers = marketEvent.RateMultipliers.Select(m => new EventRateMultiplierDto(
            m.SegmentName is null ? (Guid?)null : segments.GetOrThrow(m.SegmentName),
            ParseHelpers.ParseEnum<SimulationFlowKind>(m.FlowKind, "market event rate multiplier flowKind"),
            m.Multiplier)).ToList();
        var type = ParseHelpers.ParseEnum<MarketEventType>(marketEvent.Type, "market event type");
        var startDate = ParseHelpers.ParseDate(marketEvent.StartDate, "market event startDate");
        var endDate = ParseHelpers.ParseOptionalDate(marketEvent.EndDate, "market event endDate");

        if (index.TryGet(marketEvent.Name, out _))
        {
            tally.RecordMatched("marketEvent", marketEvent.Name);
            return;
        }

        await WriteAsync(tally, () => client.CreateMarketEventAsync(new CreateMarketEventRequest(
            marketId, type, marketEvent.Name, startDate, endDate, marketEvent.IsHypothetical, marketEvent.Magnitude,
            competitorId, marketEvent.Notes, rateMultipliers), ct));
        tally.RecordCreated("marketEvent", marketEvent.Name);
    }

    // ---------- Phase 3: channels, integrations ----------

    private static async Task<NameIndex> LoadChannelsAsync(
        ProductManagerApiClient client, Guid marketId, List<SeedChannel> channels,
        NameIndex segments, NameIndex products, SeedTally tally, CancellationToken ct)
    {
        var existing = await ListChildrenAsync(tally, marketId, page => client.ListChannelsAsync(marketId, null, page, ListPageSize, ct));
        var index = new NameIndex(existing.Select(c => (c.Name, c.Id)));

        foreach (var channel in channels)
            await Guard(tally, "channel", channel.Name, async () =>
            {
                var id = await GetOrCreateAsync(index, channel.Name, "channel", tally, async () =>
                    (await client.CreateChannelAsync(new CreateChannelRequest(
                        marketId, channel.Name, channel.Description, channel.OriginatesOpportunities, channel.CarriesOnlyListedProducts), ct)).Id);

                foreach (var reach in channel.SegmentReaches)
                {
                    var segmentId = segments.GetOrThrow(reach.SegmentName);
                    await WriteAsync(tally, () => client.SetChannelSegmentReachAsync(id,
                        new SetChannelSegmentReachRequest(segmentId, reach.Reach, reach.OriginationShare), ct));
                    tally.RecordApplied("channelSegmentReach", $"{channel.Name} / {reach.SegmentName}");
                }

                foreach (var affinity in channel.ProductAffinities)
                {
                    var productId = products.GetOrThrow(affinity.ProductName);
                    await WriteAsync(tally, () => client.SetChannelProductAffinityAsync(id, new SetChannelProductAffinityRequest(productId, affinity.Bias), ct));
                    tally.RecordApplied("channelProductAffinity", $"{channel.Name} / {affinity.ProductName}");
                }
            });

        return index;
    }

    private static async Task LoadIntegrationsAsync(
        ProductManagerApiClient client, List<SeedIntegration> integrations, NameIndex products, SeedTally tally, CancellationToken ct)
    {
        foreach (var integration in integrations)
        {
            var key = $"{integration.ProductAName} / {integration.ProductBName}";
            await Guard(tally, "integration", key, async () =>
            {
                var productAId = products.GetOrThrow(integration.ProductAName);
                var productBId = products.GetOrThrow(integration.ProductBName);

                // No natural-key upsert for integrations — the edge is symmetric, so match either
                // ordering against what a product already carries before creating a duplicate.
                var existing = await ListChildrenAsync(tally, productAId, page => client.ListIntegrationsAsync(productAId, page, ListPageSize, ct));
                var already = existing.Any(i =>
                    (i.ProductAId == productAId && i.ProductBId == productBId) ||
                    (i.ProductAId == productBId && i.ProductBId == productAId));

                if (already)
                {
                    tally.RecordMatched("integration", key);
                    return;
                }

                await WriteAsync(tally, () => client.CreateIntegrationAsync(
                    new CreateIntegrationRequest(productAId, productBId, integration.QualityScore, integration.Notes), ct));
                tally.RecordCreated("integration", key);
            });
        }
    }

    // ---------- shared helpers ----------

    private static DateTimeOffset ToUtcMidnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset? ToUtcMidnight(DateOnly? date) => date is null ? null : ToUtcMidnight(date.Value);

    private static MarketIndexes RequireMarket(MarketIndexes? market) =>
        market ?? throw new SeedReferenceException("a market entity (no market defined in the seed file)");

    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static async Task<Guid> GetOrCreateAsync(
        NameIndex index, string name, string kind, SeedTally tally, Func<Task<Guid>> create)
    {
        if (index.TryGet(name, out var id))
        {
            tally.RecordMatched(kind, name);
            return id;
        }

        var newId = tally.DryRun ? tally.Placeholder() : await create();
        index.Add(name, newId);
        tally.RecordCreated(kind, name);
        return newId;
    }

    /// <summary>Every item of a paged listing, page by page until the total is reached — a retry must
    /// see what lies beyond the first page as well, or it duplicates it (PRD6 requirement 9).</summary>
    private static async Task<List<T>> ListAllAsync<T>(Func<int, Task<PagedResponse<T>>> fetch)
    {
        var all = new List<T>();
        for (var page = 1; ; page++)
        {
            var response = await fetch(page);
            all.AddRange(response.Items);
            if (response.Items.Count == 0 || all.Count >= response.TotalCount) return all;
        }
    }

    /// <summary><see cref="ListAllAsync{T}"/> for the children of <paramref name="parentId"/> — nothing
    /// when the parent is a dry run's placeholder, which the API has never heard of.</summary>
    private static async Task<List<T>> ListChildrenAsync<T>(SeedTally tally, Guid parentId, Func<int, Task<PagedResponse<T>>> fetch) =>
        tally.IsPlaceholder(parentId) ? [] : await ListAllAsync(fetch);

    /// <summary>Builds the same indexes <see cref="LoadMarketAsync"/> returns, purely from what the
    /// API already holds for one market — creating nothing and touching no tally.</summary>
    private static async Task<MarketIndexes> BuildMarketIndexesAsync(
        ProductManagerApiClient client, Guid marketId, SeedTally tally, CancellationToken ct)
    {
        var segments = new NameIndex((await ListAllAsync(page => client.ListClientSegmentsAsync(marketId, page, ListPageSize, ct))).Select(s => (s.Name, s.Id)));
        var businessProcesses = new NameIndex((await ListAllAsync(page => client.ListBusinessProcessesAsync(marketId, null, page, ListPageSize, ct))).Select(p => (p.Name, p.Id)));
        var approaches = new NameIndex();
        await ListRemainingApproachesAsync(client, businessProcesses, approaches, [], tally, ct);
        var competitors = new NameIndex((await ListAllAsync(page => client.ListCompetitorsAsync(marketId, page, ListPageSize, ct))).Select(c => (c.Name, c.Id)));
        var products = new NameIndex((await ListAllAsync(page => client.ListProductsAsync(marketId, null, null, page, ListPageSize, ct))).Select(p => (p.Name, p.Id)));

        return new MarketIndexes(marketId, segments, businessProcesses, approaches, competitors, products);
    }

    private sealed record MarketIndexes(
        Guid MarketId, NameIndex Segments, NameIndex BusinessProcesses, NameIndex Approaches, NameIndex Competitors, NameIndex Products);

    /// <summary>
    /// Supplies the market indexes that initiatives and features resolve their by-name references
    /// against. A file with its own <c>market</c> section carries them already; a file without one
    /// (an initiatives-, features- or roadmap-only dataset) resolves against the workspace's single
    /// market instead, read from the API on first use. Lazy on purpose: a file that never names a
    /// market entity never lists markets, so a purely execution-side file works in a workspace with
    /// no market at all — and in one with several.
    /// </summary>
    private sealed class MarketResolver(ProductManagerApiClient client, Guid workspaceId, MarketIndexes? fromSeedFile)
    {
        private MarketIndexes? _resolved = fromSeedFile;
        private bool _listed;

        /// <summary>The market to resolve against, or null when the workspace holds no market at all
        /// (callers then raise the specific "no market defined" message for what they were resolving).
        /// Throws when the workspace holds several and the file did not say which.</summary>
        public async Task<MarketIndexes?> TryResolveAsync(CancellationToken ct)
        {
            if (_resolved is not null || _listed) return _resolved;
            _listed = true;

            var markets = await ListAllAsync(page => client.ListMarketsAsync(workspaceId, page, ListPageSize, ct));
            if (markets.Count == 0) return null;

            if (markets.Count > 1)
                throw SeedReferenceException.WithMessage(
                    "the seed file has no \"market\" section, but this workspace holds " +
                    $"{markets.Count} markets ({string.Join(", ", markets.Select(m => $"'{m.Name}'"))}), " +
                    "so the loader cannot tell which one its segment/product/process names belong to. " +
                    "Add a \"market\": { \"name\": \"...\" } stub naming the market to resolve against — " +
                    "a stub with only a name matches the existing market and creates nothing.");

            _resolved = await BuildMarketIndexesAsync(client, markets[0].Id, new SeedTally(), ct);
            return _resolved;
        }
    }

    /// <summary>
    /// Name → id for projects, spanning the file being loaded and, on a miss, the whole workspace.
    /// A roadmap-only file (or a <c>dependsOnProjectNames</c> edge pointing outside the file's own
    /// initiatives) must still resolve, and projects are listed per-initiative during loading, so the
    /// workspace-wide listing is the fallback — fetched once, and only when something actually misses.
    /// </summary>
    private sealed class ProjectResolver(ProductManagerApiClient client, Guid workspaceId)
    {
        private bool _listedWorkspace;

        /// <summary>The file-scoped index, which <see cref="LoadProjectsAsync"/> fills per initiative
        /// and creates into. Ids already in it win over the workspace-wide listing.</summary>
        public NameIndex Index { get; } = new();

        public async Task<Guid> ResolveAsync(string name, CancellationToken ct)
        {
            if (Index.TryGet(name, out var id)) return id;

            if (!_listedWorkspace)
            {
                _listedWorkspace = true;
                foreach (var project in await ListAllAsync(page => client.ListProjectsAsync(workspaceId, null, page, ListPageSize, ct)))
                    if (!Index.TryGet(project.Name, out _))
                        Index.Add(project.Name, project.Id);

                if (Index.TryGet(name, out id)) return id;
            }

            throw new SeedReferenceException(name);
        }
    }
}
