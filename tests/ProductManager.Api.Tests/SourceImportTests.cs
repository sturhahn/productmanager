using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using ProductManager.Domain.Imports;
using CalibrationInput = ProductManager.Engine.Calibration.CalibrationInput;

namespace ProductManager.Api.Tests;

/// <summary>
/// PRD6 — reliable source imports: records carry a stable source identity, repeats (including a
/// retry after a lost response) are no-ops, a changed terminal outcome is reported as needing a
/// correction instead of being applied, and partial updates never overwrite local work silently.
/// </summary>
public sealed class SourceImportTests : Phase3TestBase
{
    private static SourceRef Crm(string recordId, string objectKind = "Account") =>
        new("salesforce", "acme.my.salesforce.com", objectKind, recordId);

    private Task<BulkImportFirmsResponse> ImportAsync(Guid marketId, params FirmImportItem[] items) =>
        PostAsync<BulkImportFirmsResponse>("/api/firms/bulk", new BulkImportFirmsRequest(marketId, items));

    private async Task<PagedResponse<FirmDto>> FirmsAsync(Guid marketId) =>
        await GetAsync<PagedResponse<FirmDto>>($"/api/firms?marketId={marketId}");

    // ---------- Firms: identity, rename, adoption ----------

    [Fact]
    public async Task Same_named_source_accounts_stay_distinguishable_and_a_rename_moves_the_same_firm()
    {
        var world = await CreateMarketAsync();

        var first = await ImportAsync(world.MarketId,
            new FirmImportItem("Acme", world.SegmentId, Source: Crm("001A")),
            new FirmImportItem("Acme", world.SegmentId, Source: Crm("001B")));

        // The second same-named account is neither merged into the first nor silently duplicated.
        Assert.Equal(ImportOutcome.Created, first.Outcomes[0].Outcome);
        Assert.Equal(ImportOutcome.Exception, first.Outcomes[1].Outcome);
        Assert.Contains("indistinguishable", first.Outcomes[1].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(first.Outcomes[1].FirmId);
        Assert.Equal(1, first.Created);
        Assert.Equal(1, first.Exceptions);
        Assert.Equal(1, (await FirmsAsync(world.MarketId)).TotalCount);

        // A reviewed display name resolves it.
        var reviewed = await ImportAsync(world.MarketId,
            new FirmImportItem("Acme (London)", world.SegmentId, Source: Crm("001B")));
        Assert.Equal(ImportOutcome.Created, Assert.Single(reviewed.Outcomes).Outcome);

        // Renaming the source account renames the bound firm; it does not create a second one.
        var acmeId = first.Outcomes[0].FirmId!.Value;
        var renamed = await ImportAsync(world.MarketId,
            new FirmImportItem("Acme Holdings", world.SegmentId, Source: Crm("001A")));
        var rename = Assert.Single(renamed.Outcomes);
        Assert.Equal(ImportOutcome.Updated, rename.Outcome);
        Assert.Equal(acmeId, rename.FirmId);
        Assert.Equal(0, renamed.Created);
        var firms = await FirmsAsync(world.MarketId);
        Assert.Equal(2, firms.TotalCount);
        Assert.Equal("Acme Holdings", firms.Items.Single(f => f.Id == acmeId).Name);

        // An identical re-import changes nothing and says so.
        var repeat = await ImportAsync(world.MarketId,
            new FirmImportItem("Acme Holdings", world.SegmentId, Source: Crm("001A")));
        Assert.Equal(ImportOutcome.Unchanged, Assert.Single(repeat.Outcomes).Outcome);
        Assert.Equal(1, repeat.Unchanged);
        Assert.Equal(0, repeat.Updated);

        // Renaming onto another firm's name is an exception, and the stored name stays.
        var collision = await ImportAsync(world.MarketId,
            new FirmImportItem("Acme (London)", world.SegmentId, Source: Crm("001A")));
        Assert.Equal(ImportOutcome.Exception, Assert.Single(collision.Outcomes).Outcome);
        Assert.Equal("Acme Holdings", (await GetAsync<FirmDto>($"/api/firms/{acmeId}")).Name);
    }

    [Fact]
    public async Task A_name_loaded_firm_is_adopted_by_its_first_source_import_and_the_binding_is_listed()
    {
        var world = await CreateMarketAsync();
        var legacy = await ImportAsync(world.MarketId, new FirmImportItem("Beta Capital", world.SegmentId));
        var betaId = legacy.Firms.Single().Id;

        var adopted = await ImportAsync(world.MarketId,
            new FirmImportItem("Beta Capital", world.SegmentId, Source: Crm("002")));
        var outcome = Assert.Single(adopted.Outcomes);
        Assert.Equal(ImportOutcome.Updated, outcome.Outcome);
        Assert.Equal(betaId, outcome.FirmId);
        Assert.Equal(1, (await FirmsAsync(world.MarketId)).TotalCount);

        await ImportAsync(world.MarketId, new FirmImportItem("Gamma", world.SegmentId, Source: Crm("003")));

        var bindings = await GetAsync<PagedResponse<SourceBindingDto>>(
            $"/api/source-bindings?workspaceId={world.WorkspaceId}&sourceSystem=salesforce&objectKind=Account&sourceRecordId=002");
        var binding = Assert.Single(bindings.Items);
        Assert.Equal("Firm", binding.TargetType);
        Assert.Equal(betaId, binding.TargetId);
        Assert.Equal("acme.my.salesforce.com", binding.SourceInstance);

        var byTarget = await GetAsync<PagedResponse<SourceBindingDto>>($"/api/source-bindings?targetId={betaId}");
        Assert.Equal("002", Assert.Single(byTarget.Items).SourceRecordId);

        var paged = await GetAsync<PagedResponse<SourceBindingDto>>(
            $"/api/source-bindings?workspaceId={world.WorkspaceId}&pageSize=1&page=2");
        Assert.Equal(2, paged.TotalCount);
        Assert.Single(paged.Items);
    }

    // ---------- Opportunities: identity and repeat-safe lifecycle ----------

    [Fact]
    public async Task Same_day_same_scope_opportunities_with_different_source_ids_both_exist_and_reposts_create_nothing()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Northwind", EpistemicStatus.Observed, world.SegmentId));
        var opened = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);

        CreateOpportunityRequest Deal(string id) =>
            new(firm.Id, opened, null, null, [world.SettlementProcessId], Source: Crm(id, "Opportunity"));

        var one = await Client.PostAsJsonAsync("/api/opportunities", Deal("006A"), ApiFactory.Json);
        var two = await Client.PostAsJsonAsync("/api/opportunities", Deal("006B"), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        var oneDto = (await one.Content.ReadFromJsonAsync<OpportunityDto>(ApiFactory.Json))!;
        var twoDto = (await two.Content.ReadFromJsonAsync<OpportunityDto>(ApiFactory.Json))!;
        Assert.NotEqual(oneDto.Id, twoDto.Id);

        var repost = await Client.PostAsJsonAsync("/api/opportunities", Deal("006A"), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, repost.StatusCode);
        Assert.Equal(oneDto.Id, (await repost.Content.ReadFromJsonAsync<OpportunityDto>(ApiFactory.Json))!.Id);

        Assert.Equal(2, (await GetAsync<PagedResponse<OpportunityDto>>($"/api/opportunities?firmId={firm.Id}")).TotalCount);

        // Legacy (no source) still always creates.
        await PostAsync<OpportunityDto>("/api/opportunities",
            new CreateOpportunityRequest(firm.Id, opened, null, null, [world.SettlementProcessId]));
        Assert.Equal(3, (await GetAsync<PagedResponse<OpportunityDto>>($"/api/opportunities?firmId={firm.Id}")).TotalCount);
    }

    [Fact]
    public async Task Open_qualified_won_with_every_call_retried_leaves_one_opportunity_and_one_decision_fact()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Contoso", EpistemicStatus.Observed, world.SegmentId));
        var opened = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var create = new CreateOpportunityRequest(firm.Id, opened, null, null, [world.SettlementProcessId],
            Source: Crm("006C", "Opportunity"));
        var qualify = new QualifyOpportunityRequest(opened.AddDays(10));
        var decide = new DecideOpportunityRequest(OpportunityStage.Won, world.OurProductId, opened.AddDays(40),
            opened.AddDays(41));

        // Each call is sent twice, as a client that lost the first response would.
        var opportunity = await PostAsync<OpportunityDto>("/api/opportunities", create);
        Assert.Equal(opportunity.Id, (await PostAsync<OpportunityDto>("/api/opportunities", create)).Id);
        await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/qualify", qualify);
        await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/qualify", qualify);
        await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide", decide);
        var retried = await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide", decide);
        // A retry that omits the date matches the recorded decision too.
        await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide",
            decide with { At = null, RecordedAt = null });

        Assert.Equal(OpportunityStage.Won, retried.Stage);
        Assert.Equal(1, (await GetAsync<PagedResponse<OpportunityDto>>($"/api/opportunities?firmId={firm.Id}")).TotalCount);
        var facts = await GetAsync<PagedResponse<FactEventDto>>(
            $"/api/facts?workspaceId={world.WorkspaceId}&kind={FactEventKind.OpportunityDecided}");
        var fact = Assert.Single(facts.Items);

        // A changed terminal outcome is a correction, refused with a machine-readable problem type.
        var corrected = await Client.PostAsJsonAsync($"/api/opportunities/{opportunity.Id}/decide",
            new DecideOpportunityRequest(OpportunityStage.Lost, null, opened.AddDays(40)), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, corrected.StatusCode);
        var problem = (await corrected.Content.ReadFromJsonAsync<ProblemDetails>(ApiFactory.Json))!;
        Assert.Equal(ProblemTypes.CorrectionRequired, problem.Type);

        // A changed date is as well, and a plain lifecycle conflict is not a correction.
        var moved = await Client.PostAsJsonAsync($"/api/opportunities/{opportunity.Id}/decide",
            decide with { At = opened.AddDays(45) }, ApiFactory.Json);
        Assert.Equal(ProblemTypes.CorrectionRequired,
            (await moved.Content.ReadFromJsonAsync<ProblemDetails>(ApiFactory.Json))!.Type);

        var after = await GetAsync<PagedResponse<FactEventDto>>(
            $"/api/facts?workspaceId={world.WorkspaceId}&kind={FactEventKind.OpportunityDecided}");
        Assert.Equal(fact, Assert.Single(after.Items));
        Assert.Equal(OpportunityDecisionFact.WonOutcome,
            SimulationJson.Deserialize<OpportunityDecisionFact>(fact.PayloadJson!).Outcome);
        Assert.Equal(OpportunityStage.Won, (await GetAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}")).Stage);
    }

    // ---------- Facts: distinct same-day churns, repeat-safe ----------

    [Fact]
    public async Task Two_same_day_churns_with_different_source_ids_both_count_and_a_repost_appends_nothing()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Client", EpistemicStatus.Observed, world.SegmentId));
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(client.Id, world.OurProductId, null, 1.0, null, today.AddYears(-2)));

        CreateFactEventRequest Churn(string id, string customer) => new(
            world.WorkspaceId, FactEventKind.Churn, today.AddMonths(-2), "Firm", client.Id,
            SimulationJson.Serialize(new ChurnFact(world.SegmentId, client.Id, world.OurProductId, customer, "a0X" + id)),
            Source: Crm(id, "ChurnEvent"));

        var first = await Client.PostAsJsonAsync("/api/facts", Churn("C1", "001A"), ApiFactory.Json);
        var second = await Client.PostAsJsonAsync("/api/facts", Churn("C2", "001B"), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstFact = (await first.Content.ReadFromJsonAsync<FactEventDto>(ApiFactory.Json))!;

        var repost = await Client.PostAsJsonAsync("/api/facts", Churn("C1", "001A"), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, repost.StatusCode);
        Assert.Equal(firstFact.Id, (await repost.Content.ReadFromJsonAsync<FactEventDto>(ApiFactory.Json))!.Id);

        // The same source event with a different date is a correction, not a second churn.
        var changed = await Client.PostAsJsonAsync("/api/facts",
            Churn("C1", "001A") with { EffectiveDate = today.AddMonths(-1) }, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(ProblemTypes.CorrectionRequired,
            (await changed.Content.ReadFromJsonAsync<ProblemDetails>(ApiFactory.Json))!.Type);

        var churns = await GetAsync<PagedResponse<FactEventDto>>(
            $"/api/facts?workspaceId={world.WorkspaceId}&kind={FactEventKind.Churn}");
        Assert.Equal(2, churns.TotalCount);
        var provenance = SimulationJson.Deserialize<ChurnFact>(churns.Items.Single(f => f.Id == firstFact.Id).PayloadJson!);
        Assert.Equal("001A", provenance.SourceCustomerRef);

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, today));
        var input = SimulationJson.Deserialize<CalibrationInput>(await RawAsync($"/api/calibration-runs/{run.Run.Id}/input"));
        Assert.Equal(2, input.ChurnEvents.Count);
        Assert.Equal(2, run.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.ChurnHazard).SampleSize);
    }

    // ---------- Relationships: end dates, omission, episodes, consolidation ----------

    [Fact]
    public async Task An_imported_end_date_stops_exposure_and_omitting_the_relationship_later_leaves_it_untouched()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var acquired = today.AddYears(-3);
        var ended = today.AddYears(-1);

        var imported = await ImportAsync(world.MarketId,
            new FirmImportItem("Former client", world.SegmentId, Source: Crm("001F"), Relationships:
                [new FirmImportRelationship(world.OurProductId, AcquisitionDate: acquired, EndDate: ended,
                    Sources: [Crm("800F", "Contract")])]));
        var rel = Assert.Single(Assert.Single(imported.Outcomes).Relationships);
        Assert.Equal(ImportOutcome.Created, rel.Outcome);

        var stored = await GetAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{rel.RelationshipId}");
        Assert.Equal(ended, stored.EndDate);
        Assert.False(stored.IsActive);

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, today));
        var input = SimulationJson.Deserialize<CalibrationInput>(await RawAsync($"/api/calibration-runs/{run.Run.Id}/input"));
        var span = Assert.Single(input.RelationshipSpans);
        Assert.Equal(acquired, span.StartDate);
        Assert.Equal(ended, span.EndDate);

        // An incremental file that no longer lists the relationship neither ends nor deletes it.
        await ImportAsync(world.MarketId, new FirmImportItem("Former client", world.SegmentId, Source: Crm("001F")));
        var untouched = await GetAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{rel.RelationshipId}");
        Assert.Equal((acquired, ended), (untouched.AcquisitionDate, untouched.EndDate));

        // The same source record returning with a changed end date updates the edge.
        var extended = await ImportAsync(world.MarketId,
            new FirmImportItem("Former client", world.SegmentId, Source: Crm("001F"), Relationships:
                [new FirmImportRelationship(world.OurProductId, EndDate: ended.AddMonths(3), Sources: [Crm("800F", "Contract")])]));
        Assert.Equal(ImportOutcome.Updated, Assert.Single(Assert.Single(extended.Outcomes).Relationships).Outcome);
        var after = await GetAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{rel.RelationshipId}");
        Assert.Equal(ended.AddMonths(3), after.EndDate);
        Assert.Equal(acquired, after.AcquisitionDate);
    }

    [Fact]
    public async Task A_second_episode_or_an_undeclared_second_source_is_an_exception_and_deliberate_consolidation_binds()
    {
        var world = await CreateMarketAsync();
        var acquired = new DateOnly(2020, 1, 1);
        var ended = new DateOnly(2023, 6, 30);
        var contract = Crm("800A", "Contract");

        var first = await ImportAsync(world.MarketId,
            new FirmImportItem("Delta", world.SegmentId, Source: Crm("001D"), Relationships:
                [new FirmImportRelationship(world.OurProductId, AcquisitionDate: acquired, EndDate: ended, Sources: [contract])]));
        var edgeId = Assert.Single(first.Outcomes[0].Relationships).RelationshipId;

        FirmImportItem Delta(params FirmImportRelationship[] relationships) =>
            new("Delta", world.SegmentId, Source: Crm("001D"), Relationships: relationships);

        // A new source record describing a later episode on the same edge: surfaced, not merged.
        var episode = await ImportAsync(world.MarketId, Delta(
            new FirmImportRelationship(world.OurProductId, AcquisitionDate: new DateOnly(2024, 2, 1),
                Sources: [contract, Crm("800B", "Contract")])));
        var episodeOutcome = Assert.Single(episode.Outcomes[0].Relationships);
        Assert.Equal(ImportOutcome.Exception, episodeOutcome.Outcome);
        Assert.Contains("episode", episodeOutcome.Reason, StringComparison.OrdinalIgnoreCase);

        // A second source record not declared together with the bound one is not consolidated silently.
        var undeclared = await ImportAsync(world.MarketId, Delta(
            new FirmImportRelationship(world.OurProductId, AcquisitionDate: acquired, Sources: [Crm("800C", "Contract")])));
        Assert.Equal(ImportOutcome.Exception, Assert.Single(undeclared.Outcomes[0].Relationships).Outcome);

        // The same firm/product edge twice in one request.
        var twice = await ImportAsync(world.MarketId, Delta(
            new FirmImportRelationship(world.OurProductId, Sources: [contract]),
            new FirmImportRelationship(world.OurProductId, Sources: [contract])));
        Assert.Equal(ImportOutcome.Exception, twice.Outcomes[0].Relationships[1].Outcome);

        // Nothing above changed the stored edge.
        var stored = await GetAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{edgeId}");
        Assert.Equal(acquired, stored.AcquisitionDate);
        Assert.Equal(ended, stored.EndDate);

        // Declared together, with compatible dates, a second source record is consolidated onto the edge.
        var consolidated = await ImportAsync(world.MarketId, Delta(
            new FirmImportRelationship(world.OurProductId, AcquisitionDate: acquired,
                Sources: [contract, Crm("800D", "Contract")])));
        var merged = Assert.Single(consolidated.Outcomes[0].Relationships);
        Assert.NotEqual(ImportOutcome.Exception, merged.Outcome);
        Assert.Equal(edgeId, merged.RelationshipId);
        var bindings = await GetAsync<PagedResponse<SourceBindingDto>>($"/api/source-bindings?targetId={edgeId}");
        Assert.Equal(["800A", "800D"], bindings.Items.Select(b => b.SourceRecordId).Order().ToArray());
        Assert.Single((await GetAsync<PagedResponse<FirmProductRelationshipDto>>("/api/firm-relationships")).Items);
    }

    [Fact]
    public async Task The_relationships_endpoint_takes_an_end_date_and_is_repeat_safe_by_source()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Echo", EpistemicStatus.Observed, world.SegmentId));
        var request = new CreateFirmProductRelationshipRequest(firm.Id, world.OurProductId, null, 1.0, null,
            new DateOnly(2021, 1, 1), null, new DateOnly(2022, 1, 1), [Crm("800E", "Contract")]);

        var created = await Client.PostAsJsonAsync("/api/firm-relationships", request, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = (await created.Content.ReadFromJsonAsync<FirmProductRelationshipDto>(ApiFactory.Json))!;
        Assert.Equal(new DateOnly(2022, 1, 1), dto.EndDate);

        var repeat = await Client.PostAsJsonAsync("/api/firm-relationships", request, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Single((await GetAsync<PagedResponse<FirmProductRelationshipDto>>("/api/firm-relationships")).Items);
    }

    // ---------- Partial update and field ownership ----------

    [Fact]
    public async Task Absent_fields_are_preserved_a_clear_is_explicit_and_a_local_edit_is_reported_and_kept()
    {
        var world = await CreateMarketAsync();
        var source = Crm("001P");
        var created = await ImportAsync(world.MarketId,
            new FirmImportItem("Papa", world.SegmentId, EpistemicStatus.Observed, "{\"aum\":1}", "first note", Source: source));
        var id = created.Outcomes[0].FirmId!.Value;

        // Only the name: nothing else is touched, and nothing changed.
        var nameOnly = await ImportAsync(world.MarketId, new FirmImportItem("Papa", Source: source));
        Assert.Equal(ImportOutcome.Unchanged, nameOnly.Outcomes[0].Outcome);
        var firm = await GetAsync<FirmDto>($"/api/firms/{id}");
        Assert.Equal(world.SegmentId, firm.ClientSegmentId);
        Assert.Equal("first note", firm.Notes);
        Assert.NotNull(firm.AttributesJson);

        // Clearing is explicit and distinct from omission.
        var cleared = await ImportAsync(world.MarketId,
            new FirmImportItem("Papa", Source: source, ClearFields: ["notes"]));
        Assert.Equal(ImportOutcome.Updated, cleared.Outcomes[0].Outcome);
        firm = await GetAsync<FirmDto>($"/api/firms/{id}");
        Assert.Null(firm.Notes);
        Assert.Equal(world.SegmentId, firm.ClientSegmentId);

        // Someone edits the attributes locally.
        await PutAsync<FirmDto>($"/api/firms/{id}",
            new UpdateFirmRequest("Papa", EpistemicStatus.Observed, world.SegmentId, "{\"aum\":2}", null));

        var conflicted = await ImportAsync(world.MarketId,
            new FirmImportItem("Papa", AttributesJson: "{\"aum\":3}", Notes: "source note", Source: source));
        var outcome = conflicted.Outcomes[0];
        Assert.Equal(ImportOutcome.Updated, outcome.Outcome); // the unconflicted note still applies
        var conflict = Assert.Single(outcome.Conflicts);
        Assert.Equal("attributesJson", conflict.Field);
        Assert.Equal("{\"aum\":3}", conflict.IncomingValue);
        Assert.Contains("2", conflict.StoredValue);

        firm = await GetAsync<FirmDto>($"/api/firms/{id}");
        Assert.Contains("2", firm.AttributesJson);
        Assert.Equal("source note", firm.Notes);

        // An unknown clear field is malformed input.
        var bad = await Client.PostAsJsonAsync("/api/firms/bulk", new BulkImportFirmsRequest(world.MarketId,
            [new FirmImportItem("Papa", Source: source, ClearFields: ["name"])]), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task A_relationship_field_edited_locally_is_reported_and_kept()
    {
        var world = await CreateMarketAsync();
        var contract = Crm("800P", "Contract");
        var imported = await ImportAsync(world.MarketId,
            new FirmImportItem("Quebec", world.SegmentId, Source: Crm("001Q"), Relationships:
                [new FirmImportRelationship(world.OurProductId, [world.SettlementProcessId], 0.9, Sources: [contract])]));
        var edgeId = imported.Outcomes[0].Relationships[0].RelationshipId!.Value;

        await PutAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{edgeId}",
            new UpdateFirmProductRelationshipRequest([world.SettlementProcessId], 0.7));

        var again = await ImportAsync(world.MarketId,
            new FirmImportItem("Quebec", Source: Crm("001Q"), Relationships:
                [new FirmImportRelationship(world.OurProductId, PriceLevel: 0.95, Sources: [contract])]));
        var rel = Assert.Single(again.Outcomes[0].Relationships);
        Assert.Equal("priceLevel", Assert.Single(rel.Conflicts).Field);
        var stored = await GetAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{edgeId}");
        Assert.Equal(0.7, stored.PriceLevel);
        Assert.Equal(world.SettlementProcessId, Assert.Single(stored.ServedProcessIds));
    }

    // ---------- Review follow-ups ----------

    [Fact]
    public async Task A_local_edit_to_a_field_the_source_never_sent_is_caught_when_the_source_later_sends_it()
    {
        var world = await CreateMarketAsync();
        var source = Crm("001R");
        var id = (await ImportAsync(world.MarketId, new FirmImportItem("Romeo", world.SegmentId, Source: source)))
            .Outcomes[0].FirmId!.Value;

        await PutAsync<FirmDto>($"/api/firms/{id}",
            new UpdateFirmRequest("Romeo", EpistemicStatus.Observed, world.SegmentId, null, "call Bob"));

        var later = await ImportAsync(world.MarketId, new FirmImportItem("Romeo", Notes: "from CRM", Source: source));
        Assert.Equal("notes", Assert.Single(later.Outcomes[0].Conflicts).Field);
        Assert.Equal("call Bob", (await GetAsync<FirmDto>($"/api/firms/{id}")).Notes);
    }

    [Fact]
    public async Task A_local_edit_after_adopting_a_name_loaded_firm_is_caught()
    {
        var world = await CreateMarketAsync();
        var id = (await ImportAsync(world.MarketId, new FirmImportItem("Sierra", world.SegmentId, Notes: "legacy")))
            .Firms.Single().Id;
        var source = Crm("001S");
        await ImportAsync(world.MarketId, new FirmImportItem("Sierra", Source: source)); // adopts

        await PutAsync<FirmDto>($"/api/firms/{id}",
            new UpdateFirmRequest("Sierra", EpistemicStatus.Observed, world.SegmentId, null, "local"));

        var later = await ImportAsync(world.MarketId, new FirmImportItem("Sierra", Notes: "from CRM", Source: source));
        Assert.Equal("notes", Assert.Single(later.Outcomes[0].Conflicts).Field);
        Assert.Equal("local", (await GetAsync<FirmDto>($"/api/firms/{id}")).Notes);
    }

    [Fact]
    public async Task A_local_edit_to_a_relationship_field_the_source_never_sent_is_caught()
    {
        var world = await CreateMarketAsync();
        var contract = Crm("800T", "Contract");
        FirmImportItem Tango(FirmImportRelationship relationship) =>
            new("Tango", world.SegmentId, Source: Crm("001T"), Relationships: [relationship]);

        var edgeId = (await ImportAsync(world.MarketId, Tango(new FirmImportRelationship(world.OurProductId,
            AcquisitionDate: new DateOnly(2021, 1, 1), Sources: [contract])))).Outcomes[0].Relationships[0].RelationshipId!.Value;

        await PutAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{edgeId}",
            new UpdateFirmProductRelationshipRequest(null, 0.7, null, new DateOnly(2021, 1, 1)));

        var later = await ImportAsync(world.MarketId,
            Tango(new FirmImportRelationship(world.OurProductId, PriceLevel: 0.95, Sources: [contract])));
        Assert.Equal("priceLevel", Assert.Single(later.Outcomes[0].Relationships[0].Conflicts).Field);
        Assert.Equal(0.7, (await GetAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{edgeId}")).PriceLevel);
    }

    [Fact]
    public async Task A_kept_local_edit_is_silent_while_the_source_stands_still_and_reported_until_resolved_once_it_moves()
    {
        var world = await CreateMarketAsync();
        var source = Crm("001U");
        FirmImportItem Uniform(string notes) => new("Uniform", Notes: notes, Source: source);
        var id = (await ImportAsync(world.MarketId, Uniform("n1"))).Outcomes[0].FirmId!.Value;
        await PutAsync<FirmDto>($"/api/firms/{id}", new UpdateFirmRequest("Uniform", Notes: "local"));

        // The source still says n1: nothing to report, the local value stays.
        var still = (await ImportAsync(world.MarketId, Uniform("n1"))).Outcomes[0];
        Assert.Equal(ImportOutcome.Unchanged, still.Outcome);
        Assert.Empty(still.Conflicts);

        // The source moved: reported, and reported again on every import until someone resolves it.
        Assert.Single((await ImportAsync(world.MarketId, Uniform("n2"))).Outcomes[0].Conflicts);
        Assert.Single((await ImportAsync(world.MarketId, Uniform("n2"))).Outcomes[0].Conflicts);
        Assert.Equal("local", (await GetAsync<FirmDto>($"/api/firms/{id}")).Notes);

        await PutAsync<FirmDto>($"/api/firms/{id}", new UpdateFirmRequest("Uniform", Notes: "n2"));
        var resolved = (await ImportAsync(world.MarketId, Uniform("n2"))).Outcomes[0];
        Assert.Empty(resolved.Conflicts);
        Assert.Equal(ImportOutcome.Unchanged, resolved.Outcome);
    }

    [Fact]
    public async Task A_legacy_item_in_the_same_request_sees_sourced_creates_and_renames()
    {
        var world = await CreateMarketAsync();
        await ImportAsync(world.MarketId,
            new FirmImportItem("Victor", world.SegmentId, Source: Crm("001V")),
            new FirmImportItem("Victor", world.SegmentId));
        Assert.Equal(1, (await FirmsAsync(world.MarketId)).TotalCount);

        var whiskey = (await ImportAsync(world.MarketId, new FirmImportItem("Old Whiskey", world.SegmentId, Source: Crm("001W"))))
            .Outcomes[0].FirmId!.Value;
        await ImportAsync(world.MarketId,
            new FirmImportItem("New Whiskey", Source: Crm("001W")),
            new FirmImportItem("Old Whiskey", world.SegmentId));
        Assert.Equal("New Whiskey", (await GetAsync<FirmDto>($"/api/firms/{whiskey}")).Name);
    }

    [Fact]
    public async Task A_firm_item_whose_source_key_is_bound_to_another_kind_of_row_is_a_per_item_exception()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Xray", EpistemicStatus.Observed, world.SegmentId));
        var reused = Crm("REUSED");
        await PostAsync<OpportunityDto>("/api/opportunities",
            new CreateOpportunityRequest(firm.Id, DateTimeOffset.UtcNow, Source: reused));

        var import = await ImportAsync(world.MarketId,
            new FirmImportItem("Yankee", world.SegmentId, Source: reused),
            new FirmImportItem("Zulu", world.SegmentId, Source: Crm("001Z")));
        Assert.Equal(ImportOutcome.Exception, import.Outcomes[0].Outcome);
        Assert.Contains("Opportunity", import.Outcomes[0].Reason);
        Assert.Equal(ImportOutcome.Created, import.Outcomes[1].Outcome);
    }

    [Fact]
    public async Task Sources_on_the_relationships_endpoint_never_create_a_second_edge_beside_an_unbound_one()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Alfa", EpistemicStatus.Observed, world.SegmentId));
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(firm.Id, world.OurProductId));

        var sourced = await Client.PostAsJsonAsync("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(firm.Id, world.OurProductId, Sources: [Crm("800Z", "Contract")]),
            ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, sourced.StatusCode);
        Assert.Contains("bulk", (await sourced.Content.ReadFromJsonAsync<ProblemDetails>(ApiFactory.Json))!.Detail);
        Assert.Single((await GetAsync<PagedResponse<FirmProductRelationshipDto>>($"/api/firm-relationships?firmId={firm.Id}")).Items);
    }

    [Fact]
    public async Task Importing_through_one_consolidated_record_refreshes_every_record_bound_to_the_edge()
    {
        var world = await CreateMarketAsync();
        var (a, d) = (Crm("800A", "Contract"), Crm("800D", "Contract"));
        FirmImportItem Bravo(double level, params SourceRef[] sources) => new("Bravo", world.SegmentId,
            Source: Crm("001B"), Relationships: [new FirmImportRelationship(world.OurProductId, PriceLevel: level, Sources: sources)]);

        await ImportAsync(world.MarketId, Bravo(0.9, a, d));
        await ImportAsync(world.MarketId, Bravo(0.8, a));
        var throughD = await ImportAsync(world.MarketId, Bravo(0.85, d));

        var rel = throughD.Outcomes[0].Relationships[0];
        Assert.Empty(rel.Conflicts);
        Assert.Equal(0.85, (await GetAsync<FirmProductRelationshipDto>($"/api/firm-relationships/{rel.RelationshipId}")).PriceLevel);
    }

    [Fact]
    public async Task The_ledger_refuses_a_second_decision_fact_for_one_opportunity()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Charlie", EpistemicStatus.Observed, world.SegmentId));
        var opened = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var opportunity = await PostAsync<OpportunityDto>("/api/opportunities", new CreateOpportunityRequest(firm.Id, opened));
        await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide",
            new DecideOpportunityRequest(OpportunityStage.Lost, null, opened.AddDays(10)));

        // Through the generic ledger endpoint: a clean 409, not a 500, and no second fact.
        var second = await Client.PostAsJsonAsync("/api/facts", new CreateFactEventRequest(world.WorkspaceId,
            FactEventKind.OpportunityDecided, DateOnly.FromDateTime(opened.AddDays(10).UtcDateTime), "Opportunity",
            opportunity.Id, "{}"), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single((await GetAsync<PagedResponse<FactEventDto>>(
            $"/api/facts?workspaceId={world.WorkspaceId}&kind={FactEventKind.OpportunityDecided}")).Items);

        // Directly against the database, as the loser of two overlapping decide calls would meet it.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FactEvents.Add(new ProductManager.Domain.Facts.FactEvent(world.WorkspaceId, FactEventKind.OpportunityDecided,
            new DateOnly(2026, 1, 15), DateTimeOffset.UtcNow, "Opportunity", opportunity.Id, "{}"));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(StatusCodes.Status409Conflict, DomainExceptionHandler.Classify(failure).Status);

        // Other kinds about the same subject are unaffected.
        db.ChangeTracker.Clear();
        db.FactEvents.Add(new ProductManager.Domain.Facts.FactEvent(world.WorkspaceId, FactEventKind.Churn,
            new DateOnly(2026, 1, 15), DateTimeOffset.UtcNow, "Opportunity", opportunity.Id));
        db.FactEvents.Add(new ProductManager.Domain.Facts.FactEvent(world.WorkspaceId, FactEventKind.Churn,
            new DateOnly(2026, 1, 15), DateTimeOffset.UtcNow, "Opportunity", opportunity.Id));
        await db.SaveChangesAsync();
    }

    // ---------- The binding's uniqueness is the database's, not only the controller's ----------

    [Fact]
    public async Task The_unique_index_refuses_a_second_target_for_one_source_record_and_maps_to_409()
    {
        var workspace = await DefaultWorkspaceAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Add(new SourceBinding(workspace.Id, "crm", "prod", "Account", "001", "Firm", Guid.NewGuid(), DateTimeOffset.UtcNow));
        // Same record in another instance is another record.
        db.Add(new SourceBinding(workspace.Id, "crm", "sandbox", "Account", "001", "Firm", Guid.NewGuid(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        db.Add(new SourceBinding(workspace.Id, "crm", "prod", "Account", "001", "Firm", Guid.NewGuid(), DateTimeOffset.UtcNow));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(StatusCodes.Status409Conflict, DomainExceptionHandler.Classify(failure).Status);
    }
}
