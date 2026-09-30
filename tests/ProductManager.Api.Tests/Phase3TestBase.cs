using ProductManager.Api.Contracts;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using DistributionType = ProductManager.Domain.Common.DistributionType;

namespace ProductManager.Api.Tests;

/// <summary>
/// A small but complete Phase 3 world: one measured segment, a two-process catalog, our product
/// plus a module, a rival, the coverage grid, and the segment's demand edges. Enough for the choice
/// model to have something to choose between, small enough that every count in a test is checkable
/// by hand.
/// </summary>
public abstract class Phase3TestBase : ApiTestBase
{
    protected const double ProspectPool = 20;
    protected const int ClientCount = 3;

    protected sealed record MarketWorld(
        Guid WorkspaceId,
        Guid MarketId,
        Guid SegmentId,
        Guid SettlementProcessId,
        Guid ReportingProcessId,
        Guid OurProductId,
        Guid OurModuleId,
        Guid CompetitorId,
        Guid RivalProductId);

    protected async Task<MarketWorld> CreateMarketAsync()
    {
        var workspace = await DefaultWorkspaceAsync();
        var market = await PostAsync<MarketDto>("/api/markets",
            new CreateMarketRequest(workspace.Id, "Post-trade", null));

        var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Mid-size asset managers", null, 0.6, 0.5));
        await PutAsync<ClientSegmentDto>($"/api/client-segments/{segment.Id}/stock-state",
            new SetSegmentStockStateRequest(ProspectPool, ClientCount, 1000, 2000, 100, 1000, 50, 500));

        var settlement = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Settlement", null, null));
        var reporting = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Reporting", null, null));

        var ourProduct = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Core platform", null, null, null));
        await PutAsync<ProductDto>($"/api/products/{ourProduct.Id}/prices",
            new SetProductPriceRequest(new MoneyDto(1000m, "USD"), new MoneyDto(2000m, "USD"), segment.Id, null));

        var ourModule = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Reporting module", null, null, ourProduct.Id));
        await PutAsync<ProductDto>($"/api/products/{ourModule.Id}/prices",
            new SetProductPriceRequest(new MoneyDto(400m, "USD"), new MoneyDto(500m, "USD"), segment.Id, null));

        var competitor = await PostAsync<CompetitorDto>("/api/competitors",
            new CreateCompetitorRequest(market.Id, "Rival Co", null));
        var rivalProduct = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Rival platform", null, competitor.Id, null));
        await PutAsync<ProductDto>($"/api/products/{rivalProduct.Id}/prices",
            new SetProductPriceRequest(new MoneyDto(1100m, "USD"), new MoneyDto(2500m, "USD"), segment.Id, null));

        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(ourProduct.Id, settlement.Id, CoverageTier.Gold));
        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(ourProduct.Id, reporting.Id, CoverageTier.Silver));
        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(ourModule.Id, reporting.Id, CoverageTier.Gold));
        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(rivalProduct.Id, settlement.Id, CoverageTier.Silver));
        await PostAsync<CoverageDto>("/api/coverages",
            new CreateCoverageRequest(rivalProduct.Id, reporting.Id, CoverageTier.Bronze));

        // Demand: what the segment must accomplish, how much it matters, and what a workaround costs.
        await PostAsync<NeedDto>("/api/needs",
            new CreateNeedRequest(settlement.Id, 1.0, null, segment.Id));
        await PostAsync<NeedDto>("/api/needs",
            new CreateNeedRequest(reporting.Id, 0.6, null, segment.Id, false, new MoneyDto(200m, "USD")));

        return new MarketWorld(workspace.Id, market.Id, segment.Id, settlement.Id, reporting.Id,
            ourProduct.Id, ourModule.Id, competitor.Id, rivalProduct.Id);
    }

    /// <summary>An assumption set with every flow parameterised plus the choice-model knobs.</summary>
    protected async Task<AssumptionSetDto> CreateAssumptionSetAsync(MarketWorld world, string name = "Base case")
    {
        var set = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(world.WorkspaceId, name, null));

        await UpsertEngineParameterAsync(set.Id, world.SegmentId, SimulationFlowKind.NewFirmArrival,
            new DistributionSpecDto(DistributionType.Point, 1));
        await UpsertEngineParameterAsync(set.Id, world.SegmentId, SimulationFlowKind.ConversionToMarket,
            new DistributionSpecDto(DistributionType.Point, 0.15));
        await UpsertEngineParameterAsync(set.Id, world.SegmentId, SimulationFlowKind.WinRate,
            new DistributionSpecDto(DistributionType.Beta, 2, 2));
        await UpsertEngineParameterAsync(set.Id, world.SegmentId, SimulationFlowKind.ChurnHazard,
            new DistributionSpecDto(DistributionType.Point, 0.01));

        await UpsertKnobAsync(set.Id, ChoiceParameterKeys.SwitchingCostBase, 0.5);
        await UpsertKnobAsync(set.Id, ChoiceParameterKeys.WorkaroundCostWeight, 1.0);
        await UpsertKnobAsync(set.Id, ChoiceParameterKeys.NoDecisionUtility, 0.2);
        await UpsertKnobAsync(set.Id, ChoiceParameterKeys.ExpansionPropensity, 0.3);
        await UpsertKnobAsync(set.Id, ChoiceParameterKeys.PriceSensitivity(world.SegmentId), 0.5);
        await UpsertKnobAsync(set.Id, ChoiceParameterKeys.ScopeClusterCount(world.SegmentId), 1);
        await UpsertKnobAsync(set.Id, ChoiceParameterKeys.SalesCycleMu(world.SegmentId, null), 1.0);
        await UpsertKnobAsync(set.Id, ChoiceParameterKeys.SalesCycleSigma(world.SegmentId, null), 0.4);

        return await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{set.Id}");
    }

    protected Task<AssumptionSetDto> UpsertEngineParameterAsync(Guid assumptionSetId, Guid segmentId,
        SimulationFlowKind flowKind, DistributionSpecDto distribution, Guid? moduleProductId = null) =>
        PutAsync<AssumptionSetDto>($"/api/assumption-sets/{assumptionSetId}/parameters",
            new UpsertParameterRequest(distribution, ParameterProvenance.Asserted, null, segmentId, flowKind,
                moduleProductId));

    protected Task<AssumptionSetDto> UpsertKnobAsync(Guid assumptionSetId, string key, double value) =>
        PutAsync<AssumptionSetDto>($"/api/assumption-sets/{assumptionSetId}/parameters",
            new UpsertParameterRequest(new DistributionSpecDto(DistributionType.Point, value),
                ParameterProvenance.Asserted, null, Key: key));

    /// <summary>
    /// Opens and decides <paramref name="wins"/> + <paramref name="losses"/> new-business deals, each
    /// at a fresh firm, decided at <paramref name="decidedAt"/> and learned at <paramref name="recordedAt"/>
    /// (omitted: now) — which is what puts OpportunityDecided facts in the ledger.
    /// </summary>
    protected async Task DecideDealsAsync(MarketWorld world, int wins, int losses, DateTimeOffset decidedAt,
        DateTimeOffset? recordedAt = null)
    {
        for (int i = 0; i < wins + losses; i++)
        {
            var firm = await PostAsync<FirmDto>("/api/firms",
                new CreateFirmRequest(world.MarketId, $"Deal firm {Guid.NewGuid():N}", EpistemicStatus.Observed,
                    world.SegmentId));
            var opportunity = await PostAsync<OpportunityDto>("/api/opportunities",
                new CreateOpportunityRequest(firm.Id, decidedAt.AddDays(-60), null, null, [world.SettlementProcessId]));
            await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide",
                new DecideOpportunityRequest(OpportunityStage.Won, i < wins ? world.OurProductId : world.RivalProductId,
                    decidedAt, recordedAt));
        }
    }

    /// <summary>A live roadmap; firm-level runs still reference one, exactly as aggregate runs do.</summary>
    protected async Task<Guid> CreateLiveRoadmapAsync(Guid workspaceId)
    {
        var roadmap = await CreateRoadmapAsync(workspaceId, "Live plan", isLive: true);
        return roadmap.Id;
    }

    protected async Task<string> RawAsync(string url)
    {
        var response = await Client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode} from {url}");
        return await response.Content.ReadAsStringAsync();
    }
}
