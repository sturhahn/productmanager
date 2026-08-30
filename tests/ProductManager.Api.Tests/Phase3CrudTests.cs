using System.Net;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;

namespace ProductManager.Api.Tests;

/// <summary>
/// Phase 3 CRUD: the entities the firm-level choice model reads — channels, integrations,
/// firm↔product relationships and opportunities — plus the lifecycle transitions that feed the
/// facts ledger.
/// </summary>
public sealed class Phase3CrudTests : Phase3TestBase
{
    [Fact]
    public async Task Channel_round_trips_with_reach_origination_and_product_bias()
    {
        var world = await CreateMarketAsync();

        var channel = await PostAsync<ChannelDto>("/api/channels",
            new CreateChannelRequest(world.MarketId, "Prime brokers", "Introduces deals", true));
        Assert.True(channel.OriginatesOpportunities);
        Assert.False(channel.CarriesOnlyListedProducts);
        Assert.Empty(channel.SegmentReaches);

        var withReach = await PutAsync<ChannelDto>($"/api/channels/{channel.Id}/segment-reaches",
            new SetChannelSegmentReachRequest(world.SegmentId, 0.8, 0.6));
        var reach = Assert.Single(withReach.SegmentReaches);
        Assert.Equal(world.SegmentId, reach.ClientSegmentId);
        Assert.Equal(0.8, reach.Reach);
        Assert.Equal(0.6, reach.OriginationShare);

        // Upsert on the segment key: the same segment twice is a correction, not a second row.
        var reReached = await PutAsync<ChannelDto>($"/api/channels/{channel.Id}/segment-reaches",
            new SetChannelSegmentReachRequest(world.SegmentId, 0.5, 0.4));
        Assert.Equal(0.5, Assert.Single(reReached.SegmentReaches).Reach);

        var withBias = await PutAsync<ChannelDto>($"/api/channels/{channel.Id}/product-affinities",
            new SetChannelProductAffinityRequest(world.OurProductId, 0.7));
        Assert.Equal(0.7, Assert.Single(withBias.ProductAffinities).Bias);

        var renamed = await PutAsync<ChannelDto>($"/api/channels/{channel.Id}",
            new UpdateChannelRequest("Prime brokers (EMEA)", null, true, true));
        Assert.Equal("Prime brokers (EMEA)", renamed.Name);
        Assert.True(renamed.CarriesOnlyListedProducts);

        var listed = await GetAsync<PagedResponse<ChannelDto>>($"/api/channels?marketId={world.MarketId}");
        Assert.Equal(1, listed.TotalCount);

        var withoutBias = await DeleteAsync<ChannelDto>(
            $"/api/channels/{channel.Id}/product-affinities/{world.OurProductId}");
        Assert.Empty(withoutBias.ProductAffinities);

        var withoutReach = await DeleteAsync<ChannelDto>(
            $"/api/channels/{channel.Id}/segment-reaches/{world.SegmentId}");
        Assert.Empty(withoutReach.SegmentReaches);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/channels/{channel.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/channels/{channel.Id}")).StatusCode);
    }

    [Fact]
    public async Task Integration_round_trips_and_the_edge_is_symmetric()
    {
        var world = await CreateMarketAsync();

        var integration = await PostAsync<IntegrationDto>("/api/integrations",
            new CreateIntegrationRequest(world.OurProductId, world.RivalProductId, 0.6, "REST + files"));
        Assert.Equal(0.6, integration.QualityScore);

        // The same pair the other way round is the same edge, not a second one with its own score.
        var duplicate = await Client.PostAsJsonAsync("/api/integrations",
            new CreateIntegrationRequest(world.RivalProductId, world.OurProductId, 0.9), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var updated = await PutAsync<IntegrationDto>($"/api/integrations/{integration.Id}",
            new UpdateIntegrationRequest(0.9, "Streaming now"));
        Assert.Equal(0.9, updated.QualityScore);

        var listed = await GetAsync<PagedResponse<IntegrationDto>>($"/api/integrations?productId={world.RivalProductId}");
        Assert.Equal(1, listed.TotalCount);

        var invalid = await Client.PutAsJsonAsync($"/api/integrations/{integration.Id}",
            new { qualityScore = 1.4 }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.DeleteAsync($"/api/integrations/{integration.Id}")).StatusCode);
    }

    [Fact]
    public async Task Firm_relationship_records_modules_held_and_the_price_level_paid()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Northwind Capital", EpistemicStatus.Observed, world.SegmentId));

        var relationship = await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(firm.Id, world.RivalProductId,
                [world.SettlementProcessId], 0.85, new MoneyDto(850m, "USD"), new DateOnly(2024, 6, 1)));

        Assert.Equal(0.85, relationship.PriceLevel);
        Assert.Equal(world.SettlementProcessId, Assert.Single(relationship.ServedProcessIds));
        Assert.True(relationship.IsActive);

        var extended = await PutAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{relationship.Id}",
            new UpdateFirmProductRelationshipRequest(
                [world.SettlementProcessId, world.ReportingProcessId], 1.0, null, new DateOnly(2024, 6, 1)));
        Assert.Equal(2, extended.ServedProcessIds.Count);
        Assert.Equal(1.0, extended.PriceLevel);

        var ended = await PutAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{relationship.Id}",
            new UpdateFirmProductRelationshipRequest(
                [world.SettlementProcessId], 1.0, null, new DateOnly(2024, 6, 1), new DateOnly(2026, 1, 31)));
        Assert.False(ended.IsActive);

        var active = await GetAsync<PagedResponse<FirmProductRelationshipDto>>(
            $"/api/firm-relationships?firmId={firm.Id}&activeOnly=true");
        Assert.Equal(0, active.TotalCount);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.DeleteAsync($"/api/firm-relationships/{relationship.Id}")).StatusCode);
    }

    [Fact]
    public async Task Opportunity_runs_its_lifecycle_and_a_decision_reaches_the_facts_ledger()
    {
        var world = await CreateMarketAsync();
        var workspace = await DefaultWorkspaceAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Northwind Capital", EpistemicStatus.Observed, world.SegmentId));
        var channel = await PostAsync<ChannelDto>("/api/channels",
            new CreateChannelRequest(world.MarketId, "Direct", null, true));

        var opened = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
        var opportunity = await PostAsync<OpportunityDto>("/api/opportunities",
            new CreateOpportunityRequest(firm.Id, opened, channel.Id, null,
                [world.SettlementProcessId], [world.CompetitorId], "Inbound"));

        Assert.Equal(OpportunityStage.Open, opportunity.Stage);
        Assert.False(opportunity.IsCrossSell);
        Assert.Equal(world.SettlementProcessId, Assert.Single(opportunity.Scope).BusinessProcessId);
        Assert.Equal(world.CompetitorId, Assert.Single(opportunity.CompetitorIds));

        // Land-and-expand: scope grows mid-cycle.
        var expanded = await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/scope",
            new AddOpportunityScopeRequest(world.ReportingProcessId, opened.AddDays(5)));
        Assert.Equal(2, expanded.Scope.Count);

        var qualified = await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/qualify",
            new QualifyOpportunityRequest(opened.AddDays(7)));
        Assert.Equal(OpportunityStage.Qualified, qualified.Stage);

        // Qualifying twice is a state violation, not a silent no-op.
        var again = await Client.PostAsJsonAsync($"/api/opportunities/{opportunity.Id}/qualify",
            new QualifyOpportunityRequest(opened.AddDays(8)), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var decidedAt = opened.AddDays(40);
        var decided = await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide",
            new DecideOpportunityRequest(OpportunityStage.Won, world.OurProductId, decidedAt));
        Assert.Equal(OpportunityStage.Won, decided.Stage);
        Assert.True(decided.IsDecided);
        Assert.Equal(world.OurProductId, decided.WinningProductId);

        // Scope is frozen once decided.
        var lateScope = await Client.PostAsJsonAsync($"/api/opportunities/{opportunity.Id}/scope",
            new AddOpportunityScopeRequest(world.SettlementProcessId), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, lateScope.StatusCode);

        var facts = await GetAsync<PagedResponse<FactEventDto>>(
            $"/api/facts?workspaceId={workspace.Id}&kind={FactEventKind.OpportunityDecided}");
        var fact = Assert.Single(facts.Items);
        Assert.Equal(nameof(ProductManager.Domain.Market.Opportunity), fact.SubjectType);
        Assert.Equal(opportunity.Id, fact.SubjectId);
        Assert.Equal(DateOnly.FromDateTime(decidedAt.UtcDateTime), fact.EffectiveDate);

        var payload = SimulationJson.Deserialize<OpportunityDecisionFact>(fact.PayloadJson!);
        Assert.Equal(OpportunityDecisionFact.WonOutcome, payload.Outcome);
        Assert.True(payload.WinnerIsOurs);
        Assert.Equal(world.SegmentId, payload.ClientSegmentId);
        Assert.Equal(channel.Id, payload.ChannelId);
        Assert.Equal(new DateOnly(2026, 3, 2), payload.OpenedDate);
        Assert.Equal(2, payload.ScopeProcessIds.Count);
    }

    [Fact]
    public async Task A_deal_a_competitor_won_is_recorded_as_a_loss_from_our_perspective()
    {
        var world = await CreateMarketAsync();
        var workspace = await DefaultWorkspaceAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Southgate Partners", EpistemicStatus.Observed, world.SegmentId));

        var opportunity = await PostAsync<OpportunityDto>("/api/opportunities",
            new CreateOpportunityRequest(firm.Id, DateTimeOffset.UtcNow.AddDays(-30), null, null,
                [world.SettlementProcessId]));

        await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide",
            new DecideOpportunityRequest(OpportunityStage.Won, world.RivalProductId, DateTimeOffset.UtcNow));

        var facts = await GetAsync<PagedResponse<FactEventDto>>(
            $"/api/facts?workspaceId={workspace.Id}&kind={FactEventKind.OpportunityDecided}");
        var payload = SimulationJson.Deserialize<OpportunityDecisionFact>(Assert.Single(facts.Items).PayloadJson!);

        Assert.Equal(OpportunityDecisionFact.LostOutcome, payload.Outcome);
        Assert.False(payload.WinnerIsOurs);
        Assert.Equal(world.RivalProductId, payload.WinningProductId);
    }

    [Fact]
    public async Task A_cross_sell_is_an_opportunity_at_an_existing_client_pointed_at_a_module()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Existing client", EpistemicStatus.Observed, world.SegmentId));
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(firm.Id, world.OurProductId, [world.SettlementProcessId]));

        var opportunity = await PostAsync<OpportunityDto>("/api/opportunities",
            new CreateOpportunityRequest(firm.Id, DateTimeOffset.UtcNow, null, world.OurModuleId,
                [world.ReportingProcessId]));

        Assert.True(opportunity.IsCrossSell);
        Assert.Equal(world.OurModuleId, opportunity.CrossSellModuleProductId);

        var listed = await GetAsync<PagedResponse<OpportunityDto>>("/api/opportunities?isCrossSell=true");
        Assert.Equal(1, listed.TotalCount);

        var newBusiness = await GetAsync<PagedResponse<OpportunityDto>>("/api/opportunities?isCrossSell=false");
        Assert.Equal(0, newBusiness.TotalCount);
    }

    [Fact]
    public async Task Segment_approach_mix_round_trips()
    {
        var world = await CreateMarketAsync();
        var approach = await PostAsync<ApproachDto>("/api/approaches",
            new CreateApproachRequest(world.SettlementProcessId, "On chain", null));

        var withMix = await PutAsync<ClientSegmentDto>($"/api/client-segments/{world.SegmentId}/approach-mix",
            new SetSegmentApproachAdoptionRequest(approach.Id, 0.01));
        var adoption = Assert.Single(withMix.ApproachAdoptions);
        Assert.Equal(approach.Id, adoption.ApproachId);
        Assert.Equal(0.01, adoption.Share);

        var corrected = await PutAsync<ClientSegmentDto>($"/api/client-segments/{world.SegmentId}/approach-mix",
            new SetSegmentApproachAdoptionRequest(approach.Id, 0.05));
        Assert.Equal(0.05, Assert.Single(corrected.ApproachAdoptions).Share);

        var cleared = await DeleteAsync<ClientSegmentDto>(
            $"/api/client-segments/{world.SegmentId}/approach-mix/{approach.Id}");
        Assert.Empty(cleared.ApproachAdoptions);
    }

    [Fact]
    public async Task Bulk_import_creates_firms_with_relationships_and_re_importing_updates_them()
    {
        var world = await CreateMarketAsync();

        var request = new BulkImportFirmsRequest(world.MarketId, [
            new FirmImportItem("Alpha Asset Management", world.SegmentId, EpistemicStatus.Observed, null, null,
                [new FirmImportRelationship(world.RivalProductId, [world.SettlementProcessId], 0.9)]),
            new FirmImportItem("Beta Capital", world.SegmentId, EpistemicStatus.Inferred)
        ]);

        var first = await PostAsync<BulkImportFirmsResponse>("/api/firms/bulk", request);
        Assert.Equal(2, first.Created);
        Assert.Equal(0, first.Updated);
        Assert.Equal(1, first.RelationshipsWritten);

        // Natural-key matching: the same roster again corrects rather than duplicates.
        var second = await PostAsync<BulkImportFirmsResponse>("/api/firms/bulk", request);
        Assert.Equal(0, second.Created);
        Assert.Equal(2, second.Updated);

        var firms = await GetAsync<PagedResponse<FirmDto>>($"/api/firms?marketId={world.MarketId}");
        Assert.Equal(2, firms.TotalCount);

        var relationships = await GetAsync<PagedResponse<FirmProductRelationshipDto>>("/api/firm-relationships");
        var relationship = Assert.Single(relationships.Items);
        Assert.Equal(0.9, relationship.PriceLevel);
        Assert.Equal(world.SettlementProcessId, Assert.Single(relationship.ServedProcessIds));

        var unknownProduct = await Client.PostAsJsonAsync("/api/firms/bulk",
            new BulkImportFirmsRequest(world.MarketId, [
                new FirmImportItem("Gamma", world.SegmentId, EpistemicStatus.Observed, null, null,
                    [new FirmImportRelationship(Guid.NewGuid())])
            ]), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.NotFound, unknownProduct.StatusCode);
    }

    [Fact]
    public async Task Synthetic_generation_fills_the_stocks_deterministically_and_regeneration_replaces()
    {
        var world = await CreateMarketAsync();

        // One observed client and one observed prospect: the fill is what is left of the stocks.
        var client = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Observed client", EpistemicStatus.Observed, world.SegmentId));
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(client.Id, world.OurProductId, [world.SettlementProcessId]));
        await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Observed prospect", EpistemicStatus.Observed, world.SegmentId));

        var first = await PostAsync<GenerateSyntheticFirmsResponse>("/api/firms/generate-synthetic",
            new GenerateSyntheticFirmsRequest(world.MarketId));

        var fill = Assert.Single(first.Segments);
        Assert.Equal(ProspectPool, fill.ProspectTarget);
        Assert.Equal(1, fill.ObservedProspects);
        Assert.Equal(ProspectPool - 1, fill.SyntheticProspects);
        Assert.Equal(ClientCount, fill.ClientTarget);
        Assert.Equal(1, fill.ObservedClients);
        Assert.Equal(ClientCount - 1, fill.SyntheticClients);
        Assert.Equal(0, first.Removed);
        Assert.Equal(fill.SyntheticProspects + fill.SyntheticClients, first.Created);

        var synthetic = await GetAsync<PagedResponse<FirmDto>>(
            $"/api/firms?marketId={world.MarketId}&epistemicStatus={EpistemicStatus.Synthetic}&pageSize=200");
        Assert.Equal(first.Created, synthetic.TotalCount);
        var firstIds = synthetic.Items.Select(f => f.Id).OrderBy(id => id).ToList();

        // Regenerating replaces the population rather than doubling it — same ids, same count.
        var second = await PostAsync<GenerateSyntheticFirmsResponse>("/api/firms/generate-synthetic",
            new GenerateSyntheticFirmsRequest(world.MarketId));
        Assert.Equal(first.Created, second.Removed);
        Assert.Equal(first.Created, second.Created);

        var regenerated = await GetAsync<PagedResponse<FirmDto>>(
            $"/api/firms?marketId={world.MarketId}&epistemicStatus={EpistemicStatus.Synthetic}&pageSize=200");
        Assert.Equal(firstIds, regenerated.Items.Select(f => f.Id).OrderBy(id => id).ToList());

        var all = await GetAsync<PagedResponse<FirmDto>>($"/api/firms?marketId={world.MarketId}&pageSize=200");
        Assert.Equal(first.Created + 2, all.TotalCount);

        // Synthetic clients hold the flagship product, so the segment's client stock is really met.
        var relationships = await GetAsync<PagedResponse<FirmProductRelationshipDto>>(
            $"/api/firm-relationships?productId={world.OurProductId}&pageSize=200");
        Assert.Equal(fill.SyntheticClients + 1, relationships.TotalCount);
    }
}
