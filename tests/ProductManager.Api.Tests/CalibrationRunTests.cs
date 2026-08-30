using System.Net;
using System.Net.Http.Json;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using DistributionType = ProductManager.Domain.Common.DistributionType;

namespace ProductManager.Api.Tests;

/// <summary>
/// Calibration runs: the prior assumption set plus the facts ledger as of a date produce a fitted
/// set, diffable against the prior. The as-of cut is what separates a fit from hindsight.
/// </summary>
public sealed class CalibrationRunTests : Phase3TestBase
{
    [Fact]
    public async Task A_fit_moves_the_win_rate_toward_the_observed_rate_and_records_the_evidence()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);

        // Eight decided deals: seven won, one lost. The prior is Beta(2, 2) — mean 0.5.
        await RecordDecidedOpportunitiesAsync(world, wins: 7, losses: 1);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, today, "Q1 refit", "First fit"));

        Assert.Equal(today, created.Run.FactsAsOfDate);
        Assert.Equal(prior.Id, created.Run.PriorAssumptionSetId);
        Assert.NotNull(created.Run.FittedAssumptionSetId);
        Assert.True(created.Run.IsExecuted);

        var winRate = created.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate);
        Assert.Equal(world.SegmentId, winRate.ClientSegmentId);
        Assert.Equal(8, winRate.SampleSize);
        Assert.Equal(0.5, winRate.PriorMean, 6);

        // Beta(2 + 7, 2 + 1): mean 0.75, between the prior and the empirical 0.875.
        Assert.Equal(0.75, winRate.PosteriorMean, 6);
        Assert.True(winRate.PosteriorMean > winRate.PriorMean, "The evidence should have raised the win rate.");
        Assert.True(winRate.PosteriorMean < 7.0 / 8.0, "The prior should still be pulling on the estimate.");
        Assert.Equal(winRate.PosteriorMean - winRate.PriorMean, winRate.Shift, 6);
        Assert.True(winRate.PosteriorStdDev < winRate.PriorStdDev, "Evidence should have tightened the band.");
        Assert.NotNull(winRate.Prior);
        Assert.NotNull(winRate.Posterior);
        Assert.Equal(DistributionType.Beta, winRate.Posterior!.Type);
        Assert.Equal(9, winRate.Posterior.P1, 6);
        Assert.Equal(3, winRate.Posterior.P2, 6);
        Assert.Contains("opportunit", winRate.Rationale, StringComparison.OrdinalIgnoreCase);

        // The fitted set is a branch of the prior, not an edit of it.
        var fitted = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{created.Run.FittedAssumptionSetId}");
        Assert.Equal(prior.Id, fitted.ParentAssumptionSetId);

        var fittedWinRate = fitted.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate);
        Assert.Equal(ParameterProvenance.Calibrated, fittedWinRate.Provenance);
        Assert.Equal(DistributionType.Beta, fittedWinRate.Distribution.Type);
        Assert.Equal(9, fittedWinRate.Distribution.P1, 6);
        Assert.NotNull(fittedWinRate.Rationale);

        // The prior is untouched — a past run executed against it still means what it meant.
        var priorAfter = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{prior.Id}");
        var priorWinRate = priorAfter.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate);
        Assert.Equal(ParameterProvenance.Asserted, priorWinRate.Provenance);
        Assert.Equal(2, priorWinRate.Distribution.P1, 6);

        // The choice knobs the fit did not touch travel into the branch unchanged.
        Assert.Contains(fitted.Parameters, p => p.Key == ChoiceParameterKeys.SwitchingCostBase);

        // The stored run answers the same diff on its own.
        var diff = await GetAsync<CalibrationDiffDto>($"/api/calibration-runs/{created.Run.Id}/diff");
        Assert.Equal(created.Run.Id, diff.CalibrationRunId);
        Assert.Equal(created.Run.FittedAssumptionSetId, diff.FittedAssumptionSetId);
        Assert.Equal(0.75, diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate).PosteriorMean, 6);

        var listed = await GetAsync<PagedResponse<CalibrationRunDto>>(
            $"/api/calibration-runs?workspaceId={world.WorkspaceId}");
        Assert.Equal(1, listed.TotalCount);

        // The input snapshot is the reproducibility guarantee, as it is for simulation runs.
        var input = SimulationJson.Deserialize<ProductManager.Engine.Calibration.CalibrationInput>(
            await RawAsync($"/api/calibration-runs/{created.Run.Id}/input"));
        Assert.Equal(today, input.AsOfDate);
        Assert.Equal(8, input.Opportunities.Count);
    }

    [Fact]
    public async Task Facts_recorded_after_the_as_of_date_are_not_read()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);

        // Recorded now (the server stamps the recorded date), but effective a year ago.
        await RecordDecidedOpportunitiesAsync(world, wins: 7, losses: 1,
            decidedAt: DateTimeOffset.UtcNow.AddDays(-400));

        var beforeWeKnew = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var asOfYesterday = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, beforeWeKnew, "Backdated"));

        // The deals had already happened, but we had not recorded them yet: no evidence, no fit.
        Assert.DoesNotContain(asOfYesterday.Diff.Fitted, d => d.FlowKind == SimulationFlowKind.WinRate);
        var unfitted = asOfYesterday.Diff.Unfitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate);
        Assert.False(string.IsNullOrWhiteSpace(unfitted.Reason));
        Assert.Equal(DistributionType.Beta, unfitted.Distribution!.Type);
        Assert.Equal(2, unfitted.Distribution.P1, 6);

        var backdatedInput = SimulationJson.Deserialize<ProductManager.Engine.Calibration.CalibrationInput>(
            await RawAsync($"/api/calibration-runs/{asOfYesterday.Run.Id}/input"));
        Assert.Empty(backdatedInput.Opportunities);

        // The same ledger read as of today does see them.
        var asOfToday = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, DateOnly.FromDateTime(DateTime.UtcNow), "Current"));
        Assert.Equal(8, asOfToday.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate).SampleSize);
    }

    [Fact]
    public async Task Churn_and_arrival_observations_in_the_ledger_fit_their_flows()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);

        // A client we have held for two years, so churn has real exposure to divide by.
        var client = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Long-standing client", EpistemicStatus.Observed, world.SegmentId));
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(client.Id, world.OurProductId, [world.SettlementProcessId],
                1.0, null, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-2)));

        await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            world.WorkspaceId, FactEventKind.Churn, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-3),
            "Firm", client.Id, SimulationJson.Serialize(new ChurnFact(world.SegmentId, client.Id)), "Lost on price"));

        for (int month = 1; month <= 3; month++)
        {
            await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
                world.WorkspaceId, FactEventKind.SegmentArrivalObserved,
                DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-month),
                "ClientSegment", world.SegmentId,
                SimulationJson.Serialize(new SegmentArrivalFact(world.SegmentId,
                    SimulationFlowKind.NewFirmArrival, 4))));
        }

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, DateOnly.FromDateTime(DateTime.UtcNow)));

        var churn = run.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.ChurnHazard);
        Assert.Equal(1, churn.SampleSize);
        Assert.True(churn.PosteriorMean > 0);

        var arrival = run.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.NewFirmArrival);
        Assert.Equal(3, arrival.SampleSize);
        Assert.True(arrival.PosteriorMean > arrival.PriorMean,
            "Four arrivals a month should pull an asserted rate of one upward.");
    }

    /// <summary>
    /// Client-months alone are not evidence of a churn rate. A segment with a long-standing client
    /// and nothing recorded against it must leave the hazard exactly where the prior put it, appear
    /// in the diff's unfitted list with a reason, and not be counted among the parameters the run
    /// moved — the previous behaviour drove the hazard toward zero and stamped it Calibrated with a
    /// sample size of nothing.
    /// </summary>
    [Fact]
    public async Task Exposure_without_events_leaves_the_hazard_alone_and_out_of_the_moved_count()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);

        var client = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, "Long-standing client", EpistemicStatus.Observed, world.SegmentId));
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(client.Id, world.OurProductId, [world.SettlementProcessId],
                1.0, null, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-3)));

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.DoesNotContain(run.Diff.Fitted, d => d.FlowKind == SimulationFlowKind.ChurnHazard);
        var unfitted = run.Diff.Unfitted.Single(d => d.FlowKind == SimulationFlowKind.ChurnHazard);
        Assert.Contains("No observed churn events", unfitted.Reason);

        // The prior travels into the fitted branch untouched, provenance and all.
        var priorHazard = prior.Parameters.Single(p => p.FlowKind == SimulationFlowKind.ChurnHazard);
        var fitted = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{run.Run.FittedAssumptionSetId}");
        var fittedHazard = fitted.Parameters.Single(p => p.FlowKind == SimulationFlowKind.ChurnHazard);
        Assert.Equal(priorHazard.Distribution.P1, fittedHazard.Distribution.P1, 12);
        Assert.Equal(priorHazard.Distribution.P2, fittedHazard.Distribution.P2, 12);
        Assert.Equal(ParameterProvenance.Asserted, fittedHazard.Provenance);

        // "N parameters moved" must not count it either: with no events anywhere, nothing moved.
        Assert.DoesNotContain(run.Diff.Fitted, d => d.FlowKind is not null);
        Assert.Contains("0 flow parameter", fitted.Notes ?? "");
    }

    [Fact]
    public async Task A_calibration_run_needs_an_assumption_set_that_exists()
    {
        var response = await Client.PostAsJsonAsync("/api/calibration-runs",
            new CreateCalibrationRunRequest(Guid.NewGuid()), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Opens and decides <paramref name="wins"/> + <paramref name="losses"/> opportunities, which is
    /// what puts OpportunityDecided entries in the ledger for calibration to read.
    /// </summary>
    private async Task RecordDecidedOpportunitiesAsync(MarketWorld world, int wins, int losses,
        DateTimeOffset? decidedAt = null)
    {
        var decided = decidedAt ?? DateTimeOffset.UtcNow;
        var opened = decided.AddDays(-60);

        for (int i = 0; i < wins + losses; i++)
        {
            bool won = i < wins;
            var firm = await PostAsync<FirmDto>("/api/firms",
                new CreateFirmRequest(world.MarketId, $"Deal firm {i}", EpistemicStatus.Observed, world.SegmentId));

            var opportunity = await PostAsync<OpportunityDto>("/api/opportunities",
                new CreateOpportunityRequest(firm.Id, opened, null, null, [world.SettlementProcessId]));

            await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide",
                new DecideOpportunityRequest(OpportunityStage.Won,
                    won ? world.OurProductId : world.RivalProductId, decided));
        }
    }
}
