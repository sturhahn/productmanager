using System.Text.Json;
using ProductManager.Cli.Client;
using ProductManager.Cli.Seed;

namespace ProductManager.Cli.Tests;

/// <summary>
/// Seed files are meant to be loadable one dataset at a time, with by-name references resolving
/// against what the API already holds (that is how phase2/phase3 build on phase1's market). These
/// tests pin the cross-file resolution rules by running the real <see cref="SeedLoader"/> against a
/// scripted <see cref="HttpMessageHandler"/> that serves canned listings and records every write:
///
/// <list type="bullet">
/// <item>a file with no <c>market</c> section resolves market references against the workspace's
/// single market (and says so clearly when there are zero or several);</item>
/// <item>a <c>market</c> stub carrying only a name matches the existing market and creates nothing;</item>
/// <item>the approach index covers every business process in the market, not just the ones the file
/// itself lists approaches for;</item>
/// <item>project references (roadmap items, <c>dependsOnProjectNames</c>) fall back to a
/// workspace-wide project listing when the file does not mention the owning initiative.</item>
/// </list>
/// </summary>
public class SeedLoaderCrossFileReferenceTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid MarketId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondMarketId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid MidMarketSegmentId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid EnterpriseSegmentId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid OrderPickingProcessId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid ReceivingProcessId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid RfScannerApproachId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid PaperPickApproachId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private static readonly Guid ProductId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid ExistingInitiativeId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid ExistingProjectId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid ExistingFeatureId = Guid.Parse("80000000-0000-0000-0000-000000000001");
    private static readonly Guid BluewaterFirmId = Guid.Parse("90000000-0000-0000-0000-000000000001");
    private static readonly Guid PartnerReferralChannelId = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    // ---------- 1. Initiatives-only file resolves segments against the workspace's single market ----------

    [Fact]
    public async Task Initiatives_only_file_resolves_target_segments_against_the_single_existing_market()
    {
        var api = World();
        var seed = new SeedFile
        {
            Initiatives =
            [
                new SeedInitiative { Name = "Carrier Connect", TargetSegmentNames = ["Mid-Market", "Enterprise"] }
            ]
        };

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        var created = api.SingleWrite("POST", "api/initiatives");
        Assert.Contains(MidMarketSegmentId.ToString(), created.Body);
        Assert.Contains(EnterpriseSegmentId.ToString(), created.Body);
    }

    // ---------- 2. Features-only file resolves a product and an approach-bearing delta ----------

    [Fact]
    public async Task Features_only_file_resolves_productName_and_a_delta_approach_against_the_existing_market()
    {
        var api = World();
        var seed = new SeedFile
        {
            Features =
            [
                new SeedFeature
                {
                    Name = "Directed Putaway",
                    ProductName = "Acme WMS",
                    Deltas =
                    [
                        new SeedFeatureDelta
                        {
                            Kind = "Coverage",
                            BusinessProcessName = "Order Picking",
                            ApproachName = "RF Scanner Guided",
                            SegmentName = "Mid-Market",
                            ToTier = "Gold"
                        }
                    ]
                }
            ]
        };

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        var created = api.SingleWrite("POST", "api/features");
        Assert.Contains(ProductId.ToString(), created.Body);

        var deltas = api.Writes.Single(w => w.Method == "PUT" && w.Url.Contains("/deltas"));
        Assert.Contains(OrderPickingProcessId.ToString(), deltas.Body);
        Assert.Contains(RfScannerApproachId.ToString(), deltas.Body);
        Assert.Contains(MidMarketSegmentId.ToString(), deltas.Body);
    }

    // ---------- 3. A market stub (name only) resolves an approach without creating market entities ----------

    [Fact]
    public async Task Coverage_only_file_with_a_market_stub_resolves_an_approach_and_creates_no_market_entities()
    {
        var api = World();
        var seed = new SeedFile
        {
            Market = new SeedMarket
            {
                Name = "Warehouse Software",
                Coverages =
                [
                    new SeedCoverage
                    {
                        ProductName = "Acme WMS",
                        BusinessProcessName = "Order Picking",
                        ApproachName = "RF Scanner Guided",
                        SegmentName = "Mid-Market",
                        Tier = "Gold"
                    }
                ]
            }
        };

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        var coverage = api.SingleWrite("POST", "api/coverages");
        Assert.Contains(RfScannerApproachId.ToString(), coverage.Body);
        Assert.Contains(OrderPickingProcessId.ToString(), coverage.Body);

        // Nothing in the market skeleton may be (re)created by a stub reference.
        Assert.DoesNotContain(api.Writes, w => w.Method == "POST" && (
            w.Url.StartsWith("api/markets") || w.Url.StartsWith("api/client-segments") ||
            w.Url.StartsWith("api/business-processes") || w.Url.StartsWith("api/approaches") ||
            w.Url.StartsWith("api/products") || w.Url.StartsWith("api/competitors")));
    }

    // ---------- 4. Roadmap-only file plans a project owned by an initiative the file never mentions ----------

    [Fact]
    public async Task Roadmap_only_file_plans_a_project_that_exists_under_an_unmentioned_initiative()
    {
        var api = World();
        var seed = new SeedFile
        {
            Roadmap = new SeedRoadmap
            {
                Name = "2026 Plan",
                IsLive = false,
                Items = [new SeedRoadmapItem { ProjectName = "Carrier Connect v1", Start = "2026-01-01", End = "2026-03-31" }]
            }
        };

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        var planned = api.Writes.Single(w => w.Method == "POST" && w.Url.Contains("/items"));
        Assert.Contains(ExistingProjectId.ToString(), planned.Body);
    }

    // ---------- 5. dependsOnProjectNames falls back to the workspace-wide listing too ----------

    [Fact]
    public async Task Project_dependency_resolves_a_project_under_an_initiative_not_mentioned_in_the_file()
    {
        var api = World();
        var seed = new SeedFile
        {
            Initiatives = [new SeedInitiative { Name = "Freight Ops" }],
            Projects =
            [
                new SeedProject
                {
                    Name = "Carrier Connect v2",
                    InitiativeName = "Freight Ops",
                    DependsOnProjectNames = ["Carrier Connect v1"]
                }
            ]
        };

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        var dependency = api.Writes.Single(w => w.Method == "POST" && w.Url.Contains("/dependencies"));
        Assert.Contains(ExistingProjectId.ToString(), dependency.Body);
    }

    // ---------- 6. Phase 3: an opportunities-only file resolves a firm and channel from the API ----------

    [Fact]
    public async Task Opportunities_only_file_resolves_a_firm_and_channel_that_exist_only_in_the_api()
    {
        var api = World();
        var seed = new SeedFile
        {
            MarketDynamics = new SeedMarketDynamics
            {
                MarketName = "Warehouse Software",
                Opportunities =
                [
                    new SeedOpportunity
                    {
                        FirmName = "Bluewater Logistics",
                        ChannelName = "Partner Referral",
                        OpenedDate = "2026-01-05",
                        QualifiedDate = "2026-01-20",
                        DecidedDate = "2026-03-02",
                        Outcome = "Won",
                        WinningProductName = "Acme WMS",
                        ScopeProcessNames = ["Order Picking"]
                    }
                ]
            }
        };

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        var opened = api.Writes.Single(w => w.Method == "POST" && w.Url == "api/opportunities");
        Assert.Contains(BluewaterFirmId.ToString(), opened.Body);
        Assert.Contains(PartnerReferralChannelId.ToString(), opened.Body);
        Assert.Contains(OrderPickingProcessId.ToString(), opened.Body);

        var decided = api.Writes.Single(w => w.Method == "POST" && w.Url.EndsWith("/decide"));
        Assert.Contains(ProductId.ToString(), decided.Body);

        // The roster is not this file's to write — it may only be read.
        Assert.DoesNotContain(api.Writes, w => w.Url.StartsWith("api/firms"));
    }

    // ---------- 7. Phase 3: churn/arrival facts resolve segments from the API ----------

    [Fact]
    public async Task Churn_and_arrival_only_file_resolves_segments_that_exist_only_in_the_api()
    {
        var api = World();
        var seed = new SeedFile
        {
            MarketDynamics = new SeedMarketDynamics
            {
                MarketName = "Warehouse Software",
                ChurnEvents = [new SeedChurnEvent { SegmentName = "Mid-Market", EffectiveDate = "2026-02-01" }],
                ArrivalCounts =
                [
                    new SeedArrivalCount { SegmentName = "Enterprise", FlowKind = "NewFirmArrival", Month = "2026-02-01", Count = 3 }
                ]
            }
        };

        await SeedLoader.RunAsync(api.Client, seed, CancellationToken.None);

        var facts = api.Writes.Where(w => w.Method == "POST" && w.Url.StartsWith("api/facts")).ToList();
        Assert.Equal(2, facts.Count);
        Assert.Contains(facts, f => f.Body.Contains(MidMarketSegmentId.ToString()) && f.Body.Contains("Churn"));
        Assert.Contains(facts, f => f.Body.Contains(EnterpriseSegmentId.ToString()) && f.Body.Contains("NewFirmArrival"));
    }

    // ---------- 8. A workspace with no market keeps today's clear error ----------

    [Fact]
    public async Task Initiative_segment_reference_still_fails_clearly_when_the_workspace_has_no_market()
    {
        var api = World(markets: []);
        var seed = new SeedFile
        {
            Initiatives = [new SeedInitiative { Name = "Carrier Connect", TargetSegmentNames = ["Mid-Market"] }]
        };

        var ex = await Assert.ThrowsAsync<SeedReferenceException>(
            () => SeedLoader.RunAsync(api.Client, seed, CancellationToken.None));

        Assert.Contains("Mid-Market", ex.Message);
        Assert.Contains("no market", ex.Message);
    }

    // ---------- 7. Two markets and no market section: tell the author to disambiguate ----------

    [Fact]
    public async Task Two_markets_and_no_market_section_asks_the_author_for_a_market_stub()
    {
        var api = World(markets: [(MarketId, "Warehouse Software"), (SecondMarketId, "Yard Management")]);
        var seed = new SeedFile
        {
            Initiatives = [new SeedInitiative { Name = "Carrier Connect", TargetSegmentNames = ["Mid-Market"] }]
        };

        var ex = await Assert.ThrowsAsync<SeedReferenceException>(
            () => SeedLoader.RunAsync(api.Client, seed, CancellationToken.None));

        Assert.Contains("\"market\"", ex.Message);
        Assert.Contains("\"name\"", ex.Message);
        Assert.Contains("Warehouse Software", ex.Message);
        Assert.Contains("Yard Management", ex.Message);
    }

    // ---------- The scripted API ----------

    /// <summary>
    /// A small warehouse-software world already loaded in the API: one market with two segments, two
    /// processes (only one of which carries approaches), one product, one initiative and one project.
    /// Nothing here comes from the seed file under test — that is the whole point.
    /// </summary>
    private static FakeApi World(IReadOnlyList<(Guid Id, string Name)>? markets = null)
    {
        markets ??= [(MarketId, "Warehouse Software")];

        var api = new FakeApi();
        api.Get("api/workspaces/default", new { id = WorkspaceId, name = "Default", createdAt = "2026-01-01T00:00:00+00:00" });
        api.GetPage("api/markets?workspaceId=", markets.Select(m => new { id = m.Id, workspaceId = WorkspaceId, name = m.Name }));

        foreach (var market in markets)
        {
            api.GetPage($"api/client-segments?marketId={market.Id}", market.Id != MarketId ? [] : new object[]
            {
                new { id = MidMarketSegmentId, marketId = MarketId, name = "Mid-Market" },
                new { id = EnterpriseSegmentId, marketId = MarketId, name = "Enterprise" }
            });
            api.GetPage($"api/business-processes?marketId={market.Id}", market.Id != MarketId ? [] : new object[]
            {
                new { id = OrderPickingProcessId, marketId = MarketId, name = "Order Picking" },
                new { id = ReceivingProcessId, marketId = MarketId, name = "Receiving" }
            });
            api.GetPage($"api/competitors?marketId={market.Id}", []);
            api.GetPage($"api/products?marketId={market.Id}", market.Id != MarketId ? [] : new object[]
            {
                new { id = ProductId, marketId = MarketId, name = "Acme WMS" }
            });
        }

        api.GetPage($"api/approaches?businessProcessId={OrderPickingProcessId}", new object[]
        {
            new { id = RfScannerApproachId, businessProcessId = OrderPickingProcessId, name = "RF Scanner Guided" },
            new { id = PaperPickApproachId, businessProcessId = OrderPickingProcessId, name = "Paper Pick List" }
        });
        api.GetPage($"api/approaches?businessProcessId={ReceivingProcessId}", []);

        // Phase 3: a firm and a channel loaded by earlier files, plus the empty ledgers an
        // opportunities/churn/arrival file checks before appending.
        api.GetPage($"api/firms?marketId={MarketId}", new object[]
        {
            new { id = BluewaterFirmId, marketId = MarketId, name = "Bluewater Logistics" }
        });
        api.GetPage($"api/channels?marketId={MarketId}", new object[]
        {
            new { id = PartnerReferralChannelId, marketId = MarketId, name = "Partner Referral" }
        });
        api.GetPage("api/opportunities?firmId=", []);
        api.GetPage("api/facts?", []);
        api.GetPage("api/covariates?workspaceId=", []);

        api.GetPage("api/teams?workspaceId=", []);
        api.GetPage("api/initiatives?workspaceId=", new object[]
        {
            new { id = ExistingInitiativeId, workspaceId = WorkspaceId, name = "Freight Ops" }
        });
        api.GetPage("api/features?workspaceId=", new object[]
        {
            new { id = ExistingFeatureId, workspaceId = WorkspaceId, name = "Yard Check-In" }
        });
        api.GetPage("api/roadmaps?workspaceId=", []);
        api.GetPage("api/coverages?productId=", []);
        api.GetPage("api/needs?", []);

        // "Carrier Connect v1" lives under an initiative no test file mentions: it is only reachable
        // through the workspace-wide listing, never through the per-initiative one.
        api.GetPage("api/projects?workspaceId=", new object[]
        {
            new { id = ExistingProjectId, initiativeId = ExistingInitiativeId, name = "Carrier Connect v1" }
        });
        api.GetPage($"api/projects?initiativeId={ExistingInitiativeId}", []);

        return api;
    }

    private sealed record Recorded(string Method, string Url, string Body);

    /// <summary>
    /// Serves canned JSON for GET listings (longest registered URL prefix wins) and records every
    /// write, answering it with a fresh id — enough for the loader, which only ever reads <c>id</c>
    /// off a create response.
    /// </summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        private readonly List<(string Prefix, string Json)> _responses = [];

        public List<Recorded> Writes { get; } = [];

        public ProductManagerApiClient Client => new(new HttpClient(this) { BaseAddress = new Uri("http://fake.local/") });

        public void Get(string urlPrefix, object payload) =>
            _responses.Add((urlPrefix, JsonSerializer.Serialize(payload, ProductManagerApiClient.JsonOptions)));

        public void GetPage(string urlPrefix, IEnumerable<object> items)
        {
            var materialized = items.ToList();
            Get(urlPrefix, new { items = materialized, page = 1, pageSize = 200, totalCount = materialized.Count, totalPages = 1 });
        }

        public Recorded SingleWrite(string method, string urlPrefix) =>
            Writes.Single(w => w.Method == method && w.Url.StartsWith(urlPrefix, StringComparison.OrdinalIgnoreCase));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.PathAndQuery.TrimStart('/');

            if (request.Method == HttpMethod.Get)
            {
                var match = _responses
                    .Where(r => url.StartsWith(r.Prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.Prefix.Length)
                    .Select(r => r.Json)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"FakeApi has no canned response for GET {url}");
                return Respond(match);
            }

            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Writes.Add(new Recorded(request.Method.Method, url, body));
            return Respond($"{{\"id\":\"{Guid.NewGuid()}\"}}");
        }

        private static HttpResponseMessage Respond(string json) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }
}
