using System.Net;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Domain.Common;

namespace ProductManager.Api.Tests;

/// <summary>
/// Phase 2 market-side CRUD: the write path the CLI, the web app and the seed loader all go through.
/// </summary>
public sealed class Phase2MarketCrudTests : ApiTestBase
{
    [Fact]
    public async Task Market_updates_and_deletes()
    {
        var workspace = await DefaultWorkspaceAsync();
        var market = await PostAsync<MarketDto>("/api/markets",
            new CreateMarketRequest(workspace.Id, "Post-trade", null));

        var updated = await PutAsync<MarketDto>($"/api/markets/{market.Id}",
            new UpdateMarketRequest("Post-trade EMEA", "Clearing and settlement"));
        Assert.Equal("Post-trade EMEA", updated.Name);
        Assert.Equal("Clearing and settlement", updated.Description);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/markets/{market.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/markets/{market.Id}")).StatusCode);
    }

    [Fact]
    public async Task Catalog_entities_update_and_delete()
    {
        var market = await CreateMarketAsync();

        var process = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Settlement", null, null));
        var renamedProcess = await PutAsync<BusinessProcessDto>($"/api/business-processes/{process.Id}",
            new UpdateBusinessProcessRequest("Settlement & confirmation", "Post-execution", null));
        Assert.Equal("Settlement & confirmation", renamedProcess.Name);

        var approach = await PostAsync<ApproachDto>("/api/approaches",
            new CreateApproachRequest(process.Id, "Via partner", null));
        var renamedApproach = await PutAsync<ApproachDto>($"/api/approaches/{approach.Id}",
            new UpdateApproachRequest("Direct messaging", "No intermediary"));
        Assert.Equal("Direct messaging", renamedApproach.Name);

        var competitor = await PostAsync<CompetitorDto>("/api/competitors",
            new CreateCompetitorRequest(market.Id, "Rival Co", null));
        var renamedCompetitor = await PutAsync<CompetitorDto>($"/api/competitors/{competitor.Id}",
            new UpdateCompetitorRequest("Rival Corp", "Aggressive on price"));
        Assert.Equal("Aggressive on price", renamedCompetitor.StrategyNotes);

        var product = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Core platform", null, null, null));
        var renamedProduct = await PutAsync<ProductDto>($"/api/products/{product.Id}",
            new UpdateProductRequest("Core platform v2", "Our flagship", null));
        Assert.Equal("Core platform v2", renamedProduct.Name);

        var priced = await PutAsync<ProductDto>($"/api/products/{product.Id}/prices",
            new SetProductPriceRequest(new MoneyDto(1000m, "USD"), new MoneyDto(5000m, "USD"), null, null));
        Assert.Single(priced.Prices);

        var unpriced = await DeleteAsync<ProductDto>($"/api/products/{product.Id}/prices");
        Assert.Empty(unpriced.Prices);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/approaches/{approach.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.DeleteAsync($"/api/business-processes/{process.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.DeleteAsync($"/api/competitors/{competitor.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_competitor_that_still_owns_products_is_a_conflict()
    {
        var market = await CreateMarketAsync();
        var competitor = await PostAsync<CompetitorDto>("/api/competitors",
            new CreateCompetitorRequest(market.Id, "Rival Co", null));
        await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Their platform", null, competitor.Id, null));

        var response = await Client.DeleteAsync($"/api/competitors/{competitor.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Client_segment_carries_its_stock_state_and_module_penetrations()
    {
        var market = await CreateMarketAsync();
        var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Mid-size asset managers", null));

        // A segment nobody has measured yet is honestly all-zero, not silently defaulted.
        Assert.Equal(0, segment.StockState.InitialClientCount);
        Assert.Empty(segment.ModulePenetrations);

        var withStock = await PutAsync<ClientSegmentDto>($"/api/client-segments/{segment.Id}/stock-state",
            new SetSegmentStockStateRequest(
                InitialProspectPool: 400,
                InitialClientCount: 25,
                ArpuPerMonth: 8000,
                ImplementationFee: 40000,
                ServicingCostPerClient: 1200,
                InfraFixedCostPerMonth: 50000,
                InfraCostPerClient: 300,
                RdDragPerMonth: 20000));

        Assert.Equal(400, withStock.StockState.InitialProspectPool);
        Assert.Equal(25, withStock.StockState.InitialClientCount);
        Assert.Equal(8000, withStock.StockState.ArpuPerMonth);
        Assert.Equal(20000, withStock.StockState.RdDragPerMonth);

        var moduleProduct = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Reporting module", null, null, null));

        var withModule = await PutAsync<ClientSegmentDto>($"/api/client-segments/{segment.Id}/module-penetrations",
            new SetSegmentModulePenetrationRequest(moduleProduct.Id, 0.4, 1500));
        var penetration = Assert.Single(withModule.ModulePenetrations);
        Assert.Equal(moduleProduct.Id, penetration.ModuleProductId);
        Assert.Equal(0.4, penetration.Penetration);

        // Upsert, not duplicate.
        var reset = await PutAsync<ClientSegmentDto>($"/api/client-segments/{segment.Id}/module-penetrations",
            new SetSegmentModulePenetrationRequest(moduleProduct.Id, 0.6, 1800));
        Assert.Equal(0.6, Assert.Single(reset.ModulePenetrations).Penetration);

        var removed = await DeleteAsync<ClientSegmentDto>(
            $"/api/client-segments/{segment.Id}/module-penetrations/{moduleProduct.Id}");
        Assert.Empty(removed.ModulePenetrations);

        var refetched = await GetAsync<ClientSegmentDto>($"/api/client-segments/{segment.Id}");
        Assert.Equal(25, refetched.StockState.InitialClientCount);
    }

    [Fact]
    public async Task Coverage_post_upserts_on_its_key_rather_than_duplicating()
    {
        var market = await CreateMarketAsync();
        var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Hedge funds", null));
        var process = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Settlement", null, null));
        var product = await PostAsync<ProductDto>("/api/products",
            new CreateProductRequest(market.Id, "Core platform", null, null, null));

        var first = await PostAsync<CoverageDto>("/api/coverages", new CreateCoverageRequest(
            product.Id, process.Id, CoverageTier.Bronze, false, 0.6, null, segment.Id, null, "Check-the-box"));

        var second = await PostAsync<CoverageDto>("/api/coverages", new CreateCoverageRequest(
            product.Id, process.Id, CoverageTier.Gold, false, 0.9, null, segment.Id, null, "Shipped the rewrite"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(CoverageTier.Gold, second.Tier);
        Assert.Equal(0.9, second.Confidence);
        Assert.Equal("Shipped the rewrite", second.Rationale);

        var list = await GetAsync<PagedResponse<CoverageDto>>($"/api/coverages?productId={product.Id}");
        Assert.Equal(1, list.TotalCount);

        // A different segment is a different assessment, not the same one.
        var otherSegment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Pension funds", null));
        var third = await PostAsync<CoverageDto>("/api/coverages", new CreateCoverageRequest(
            product.Id, process.Id, CoverageTier.Silver, false, 1.0, null, otherSegment.Id));
        Assert.NotEqual(first.Id, third.Id);

        var updated = await PutAsync<CoverageDto>($"/api/coverages/{first.Id}",
            new UpdateCoverageRequest(CoverageTier.NotPlaying, true));
        Assert.True(updated.IsUnknown);
        Assert.Null(updated.NumericScore);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/coverages/{first.Id}")).StatusCode);
        var after = await GetAsync<PagedResponse<CoverageDto>>($"/api/coverages?productId={product.Id}");
        Assert.Equal(1, after.TotalCount);
    }

    [Fact]
    public async Task Firm_round_trips_through_create_read_update_delete()
    {
        var market = await CreateMarketAsync();
        var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Hedge funds", null));

        var created = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(market.Id, "Acme Capital", EpistemicStatus.Observed, segment.Id,
                """{"aum":"3bn"}""", "Met at the conference"));
        Assert.Equal(EpistemicStatus.Observed, created.EpistemicStatus);
        Assert.Equal(segment.Id, created.ClientSegmentId);

        var updated = await PutAsync<FirmDto>($"/api/firms/{created.Id}",
            new UpdateFirmRequest("Acme Capital LLP", EpistemicStatus.Inferred, null, null, null));
        Assert.Equal("Acme Capital LLP", updated.Name);
        Assert.Equal(EpistemicStatus.Inferred, updated.EpistemicStatus);
        Assert.Null(updated.ClientSegmentId);

        var filtered = await GetAsync<PagedResponse<FirmDto>>(
            $"/api/firms?marketId={market.Id}&epistemicStatus=Inferred");
        Assert.Equal(1, filtered.TotalCount);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/firms/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/firms/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Need_round_trips_and_belongs_to_exactly_one_subject()
    {
        var market = await CreateMarketAsync();
        var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Hedge funds", null));
        var process = await PostAsync<BusinessProcessDto>("/api/business-processes",
            new CreateBusinessProcessRequest(market.Id, "Reconciliation", null, null));

        var created = await PostAsync<NeedDto>("/api/needs", new CreateNeedRequest(
            process.Id, 0.8, null, segment.Id, false, new MoneyDto(20000m, "USD"), null, "Operates in EMEA",
            "From the last three calls"));
        Assert.Equal(segment.Id, created.ClientSegmentId);
        Assert.Equal(20000m, created.WorkaroundCostPerPeriod!.Amount);

        var updated = await PutAsync<NeedDto>($"/api/needs/{created.Id}",
            new UpdateNeedRequest(0.95, true, null, null, null, null));
        Assert.True(updated.IsHard);
        Assert.Null(updated.WorkaroundCostPerPeriod);
        Assert.Equal(0.95, updated.ImportanceWeight);

        var bothSubjects = await Client.PostAsJsonAsync("/api/needs",
            new { businessProcessId = process.Id, importanceWeight = 0.5, firmId = Guid.NewGuid(), clientSegmentId = segment.Id },
            ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, bothSubjects.StatusCode);

        var neitherSubject = await Client.PostAsJsonAsync("/api/needs",
            new { businessProcessId = process.Id, importanceWeight = 0.5 }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, neitherSubject.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/needs/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Market_event_round_trips_with_its_rate_multipliers()
    {
        var market = await CreateMarketAsync();
        var segment = await PostAsync<ClientSegmentDto>("/api/client-segments",
            new CreateClientSegmentRequest(market.Id, "Hedge funds", null));

        var created = await PostAsync<MarketEventDto>("/api/market-events", new CreateMarketEventRequest(
            market.Id, MarketEventType.ProductionIncident, "Rival outage", new DateOnly(2026, 3, 1),
            new DateOnly(2026, 5, 31), true, 1.0, null, null,
            [new EventRateMultiplierDto(segment.Id, SimulationFlowKind.WinRate, 1.4)]));

        Assert.True(created.IsHypothetical);
        var multiplier = Assert.Single(created.RateMultipliers);
        Assert.Equal(SimulationFlowKind.WinRate, multiplier.FlowKind);
        Assert.Equal(1.4, multiplier.Multiplier);

        var updated = await PutAsync<MarketEventDto>($"/api/market-events/{created.Id}",
            new UpdateMarketEventRequest(MarketEventType.ForcedMigration, "Rival forced migration",
                new DateOnly(2026, 4, 1), null, false, null, null, null,
                [
                    new EventRateMultiplierDto(null, SimulationFlowKind.NewFirmArrival, 1.2),
                    new EventRateMultiplierDto(segment.Id, SimulationFlowKind.ChurnHazard, 0.8)
                ]));

        Assert.False(updated.IsHypothetical);
        Assert.Equal(2, updated.RateMultipliers.Count);
        Assert.Null(updated.EndDate);

        var hypothetical = await GetAsync<PagedResponse<MarketEventDto>>(
            $"/api/market-events?marketId={market.Id}&isHypothetical=true");
        Assert.Equal(0, hypothetical.TotalCount);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.DeleteAsync($"/api/market-events/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Covariate_round_trips_and_a_half_specified_future_path_is_rejected()
    {
        var workspace = await DefaultWorkspaceAsync();

        var created = await PostAsync<CovariateDto>("/api/covariates",
            new CreateCovariateRequest(workspace.Id, "Equity index", "MSCI World", "points"));
        Assert.False(created.HasGbmSpec);

        var withPath = await PutAsync<CovariateDto>($"/api/covariates/{created.Id}",
            new UpdateCovariateRequest("Equity index", "MSCI World", "points", 100, 0.005, 0.04));
        Assert.True(withPath.HasGbmSpec);
        Assert.Equal(100, withPath.GbmStart);

        // A half-specified stochastic process is not a process.
        var halfSpecified = await Client.PutAsJsonAsync($"/api/covariates/{created.Id}",
            new { name = "Equity index", gbmStart = 100.0 }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, halfSpecified.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/covariates/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Facts_append_and_the_covariate_history_reads_them_back()
    {
        var workspace = await DefaultWorkspaceAsync();
        var covariate = await PostAsync<CovariateDto>("/api/covariates",
            new CreateCovariateRequest(workspace.Id, "Equity index", null, "points"));

        await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.CovariateObserved, new DateOnly(2026, 1, 31),
            FactMapping.CovariateSubjectType, covariate.Id, """{"value": 100.5}""", "January close"));
        await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.CovariateObserved, new DateOnly(2026, 2, 28),
            FactMapping.CovariateSubjectType, covariate.Id, """{"value": 104.25}"""));

        var history = await GetAsync<CovariateHistoryDto>($"/api/covariates/{covariate.Id}/history");
        Assert.Equal(2, history.Observations.Count);
        Assert.Equal(100.5, history.Observations[0].Value);
        Assert.Equal(new DateOnly(2026, 2, 28), history.Observations[1].EffectiveDate);
        Assert.Equal("points", history.Unit);

        // A later correction for the same effective date wins; the ledger keeps both rows.
        await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.CovariateObserved, new DateOnly(2026, 1, 31),
            FactMapping.CovariateSubjectType, covariate.Id, """{"value": 101.0}""", "Restated"));

        var corrected = await GetAsync<CovariateHistoryDto>($"/api/covariates/{covariate.Id}/history");
        Assert.Equal(2, corrected.Observations.Count);
        Assert.Equal(101.0, corrected.Observations[0].Value);

        var ledger = await GetAsync<PagedResponse<FactEventDto>>(
            $"/api/facts?workspaceId={workspace.Id}&subjectId={covariate.Id}");
        Assert.Equal(3, ledger.TotalCount);

        var windowed = await GetAsync<CovariateHistoryDto>(
            $"/api/covariates/{covariate.Id}/history?effectiveFrom=2026-02-01");
        Assert.Single(windowed.Observations);

        // A covariate reading without a numeric value would be unreadable history.
        var noValue = await Client.PostAsJsonAsync("/api/facts", new
        {
            workspaceId = workspace.Id,
            kind = "CovariateObserved",
            effectiveDate = "2026-03-31",
            subjectType = FactMapping.CovariateSubjectType,
            subjectId = covariate.Id,
            payloadJson = """{"note": "up a bit"}"""
        }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, noValue.StatusCode);
    }

    private async Task<MarketDto> CreateMarketAsync()
    {
        var workspace = await DefaultWorkspaceAsync();
        return await PostAsync<MarketDto>("/api/markets",
            new CreateMarketRequest(workspace.Id, "Post-trade", null));
    }
}
