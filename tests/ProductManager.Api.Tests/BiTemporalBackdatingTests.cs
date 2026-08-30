using System.Net;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;

namespace ProductManager.Api.Tests;

/// <summary>
/// The import / backfill path of the bi-temporal ledger. "What did we know as of date D" is only
/// answerable if imported history carries the recorded date it actually had; stamping every loaded
/// row with the loader's wall clock makes every backdated question return an empty ledger, which is
/// indistinguishable from "we knew nothing" and is how a calibration ends up fitting zero evidence.
/// </summary>
public sealed class BiTemporalBackdatingTests : Phase3TestBase
{
    private static readonly DateTimeOffset LongAgo = new(2025, 3, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_imported_fact_carries_the_recorded_date_it_was_imported_with()
    {
        var workspace = await DefaultWorkspaceAsync();

        var fact = await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.PricingChanged, new DateOnly(2025, 3, 1),
            Notes: "Imported from the CRM export", RecordedAt: LongAgo));

        Assert.Equal(LongAgo, fact.RecordedDate);

        // And it stays that way when read back: the ledger is append-only, not re-stamped.
        var reread = await GetAsync<FactEventDto>($"/api/facts/{fact.Id}");
        Assert.Equal(LongAgo, reread.RecordedDate);
    }

    [Fact]
    public async Task A_backdated_fact_is_invisible_to_a_cutoff_before_it_and_visible_to_one_after()
    {
        var workspace = await DefaultWorkspaceAsync();

        var imported = await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.CompetitorShippedFeature, new DateOnly(2025, 2, 1),
            Notes: "Backfilled", RecordedAt: LongAgo));

        // Recorded live, right now, for the same effective date.
        var live = await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.CompetitorShippedFeature, new DateOnly(2025, 2, 1), Notes: "Noticed today"));

        var before = await ListAsync(workspace.Id, LongAgo.AddDays(-1));
        Assert.DoesNotContain(before, f => f.Id == imported.Id);
        Assert.DoesNotContain(before, f => f.Id == live.Id);

        var after = await ListAsync(workspace.Id, LongAgo.AddDays(1));
        Assert.Contains(after, f => f.Id == imported.Id);
        Assert.DoesNotContain(after, f => f.Id == live.Id); // we only learned this one today

        var now = await ListAsync(workspace.Id, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Contains(now, f => f.Id == imported.Id);
        Assert.Contains(now, f => f.Id == live.Id);
    }

    [Fact]
    public async Task A_recorded_date_in_the_future_is_refused()
    {
        var workspace = await DefaultWorkspaceAsync();

        var response = await Client.PostAsJsonAsync("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.PricingChanged, new DateOnly(2025, 3, 1),
            RecordedAt: DateTimeOffset.UtcNow.AddDays(1)), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("recordedAt", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The effective date is unconstrained by the recorded date — a fact may be recorded long after
    /// it was true (a backfill) or before (a scheduled price change we already know about).
    /// </summary>
    [Fact]
    public async Task Backdating_the_recorded_date_does_not_constrain_the_effective_date()
    {
        var workspace = await DefaultWorkspaceAsync();

        var afterTheFact = await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.PricingChanged, new DateOnly(2024, 1, 1), RecordedAt: LongAgo));
        Assert.Equal(new DateOnly(2024, 1, 1), afterTheFact.EffectiveDate);

        var announcedInAdvance = await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            workspace.Id, FactEventKind.PricingChanged, new DateOnly(2026, 12, 1), RecordedAt: LongAgo));
        Assert.Equal(new DateOnly(2026, 12, 1), announcedInAdvance.EffectiveDate);
        Assert.Equal(LongAgo, announcedInAdvance.RecordedDate);
    }

    // ---------- The decision path ----------

    [Fact]
    public async Task An_imported_decision_is_recorded_when_we_learned_it_and_a_fit_as_of_then_reads_it()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);

        // Eight deals closed and known about a year ago, loaded into the system today.
        var decided = DateTimeOffset.UtcNow.AddDays(-400);
        await ImportDecidedOpportunitiesAsync(world, wins: 7, losses: 1, decidedAt: decided, recordedAt: decided);

        var asOf = DateOnly.FromDateTime(decided.UtcDateTime).AddDays(1);
        var fit = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, asOf, "As of the import's own date"));

        var winRate = fit.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate);
        Assert.Equal(8, winRate.SampleSize);
        Assert.Equal(0.75, winRate.PosteriorMean, 6);

        // And a cut before we knew them still sees nothing: backdating is honest, not universal.
        var earlier = DateOnly.FromDateTime(decided.UtcDateTime).AddDays(-1);
        var blind = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, earlier, "Before we knew"));
        Assert.DoesNotContain(blind.Diff.Fitted, d => d.FlowKind == SimulationFlowKind.WinRate);
    }

    [Fact]
    public async Task Deciding_with_a_future_recorded_date_is_refused_and_leaves_the_deal_open()
    {
        var world = await CreateMarketAsync();
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Deal firm", EpistemicStatus.Observed, world.SegmentId));
        var opportunity = await PostAsync<OpportunityDto>("/api/opportunities",
            new CreateOpportunityRequest(firm.Id, DateTimeOffset.UtcNow.AddDays(-30), null, null,
                [world.SettlementProcessId]));

        var response = await Client.PostAsJsonAsync($"/api/opportunities/{opportunity.Id}/decide",
            new DecideOpportunityRequest(OpportunityStage.Won, world.OurProductId, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(2)), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Refused before anything was written: the deal is untouched and the ledger is empty.
        var reread = await GetAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}");
        Assert.NotEqual(OpportunityStage.Won, reread.Stage);
        var facts = await ListAsync(world.WorkspaceId, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.DoesNotContain(facts, f => f.Kind == FactEventKind.OpportunityDecided);
    }

    // ---------- Helpers ----------

    private async Task<IReadOnlyList<FactEventDto>> ListAsync(Guid workspaceId, DateTimeOffset knownAsOf)
    {
        string cutoff = Uri.EscapeDataString(knownAsOf.ToString("O"));
        var page = await GetAsync<PagedResponse<FactEventDto>>(
            $"/api/facts?workspaceId={workspaceId}&knownAsOf={cutoff}&pageSize=200");
        return page.Items;
    }

    private async Task ImportDecidedOpportunitiesAsync(
        MarketWorld world, int wins, int losses, DateTimeOffset decidedAt, DateTimeOffset recordedAt)
    {
        for (int i = 0; i < wins + losses; i++)
        {
            bool won = i < wins;
            var firm = await PostAsync<FirmDto>("/api/firms",
                new CreateFirmRequest(world.MarketId, $"Imported firm {i}", EpistemicStatus.Observed, world.SegmentId));

            var opportunity = await PostAsync<OpportunityDto>("/api/opportunities",
                new CreateOpportunityRequest(firm.Id, decidedAt.AddDays(-60), null, null,
                    [world.SettlementProcessId]));

            await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide",
                new DecideOpportunityRequest(OpportunityStage.Won,
                    won ? world.OurProductId : world.RivalProductId, decidedAt, recordedAt));
        }
    }
}
