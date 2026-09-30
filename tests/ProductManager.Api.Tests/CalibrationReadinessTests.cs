using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using ProductManager.Api.Contracts;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Simulation;
using ProductManager.Domain.Common;
using DistributionType = ProductManager.Domain.Common.DistributionType;

namespace ProductManager.Api.Tests;

/// <summary>
/// PRD8: calibration readiness. Evidence reuse is refused before fitting; the readiness report says,
/// per parameter and segment, what evidence moved it, what is missing, and what is merely asserted.
/// Test names carry the PRD8 acceptance criterion (AC) or requirement (R) they pin.
/// </summary>
public sealed class CalibrationReadinessTests : Phase3TestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // ---------- AC1 / R3: evidence reuse ----------

    [Fact]
    public async Task AC1_Independent_uniform_prior_fits_Beta_7_5_and_refitting_the_same_ten_deals_is_refused_before_fitting()
    {
        var world = await CreateMarketAsync();
        var prior = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 1, 1),
            new PriorEvidenceDto(PriorEvidenceBasis.Independent, Population: "flat prior"));

        // Ten deals decided and learned 100 days ago.
        await DecideDealsAsync(world, wins: 6, losses: 4, decidedAt: Now.AddDays(-100), recordedAt: Now.AddDays(-100));
        var firstAsOf = Today.AddDays(-90);
        var first = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, firstAsOf, "First fit"));

        var posterior = first.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate).Posterior!;
        Assert.Equal((7.0, 5.0), (posterior.P1, posterior.P2));

        // The fit stamps what it read on the parameter it moved.
        var fitted = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{first.Run.FittedAssumptionSetId}");
        var stamp = fitted.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate).PriorEvidence!;
        Assert.Equal(PriorEvidenceBasis.Observations, stamp.Basis);
        Assert.Equal(firstAsOf, stamp.RecordedThrough);
        Assert.Equal(first.Run.Id, stamp.SourceCalibrationRunId);
        // An Independent root has no observation period to inherit; the dates this fit read are in the text.
        Assert.Null(stamp.ObservedFrom);
        Assert.Contains($"{DateOnly.FromDateTime(Now.AddDays(-100).UtcDateTime):yyyy-MM-dd}", stamp.Population);

        // Four new deals, learned today.
        await DecideDealsAsync(world, wins: 3, losses: 1, decidedAt: Now.AddDays(-10));
        int setsBefore = (await GetAsync<PagedResponse<AssumptionSetDto>>(
            $"/api/assumption-sets?workspaceId={world.WorkspaceId}")).TotalCount;

        // Fitting the fitted set over the whole ledger would count the first ten deals twice.
        var reuse = await Client.PostAsJsonAsync("/api/calibration-runs",
            new CreateCalibrationRunRequest(fitted.Id, Today, "Double count"), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, reuse.StatusCode);
        var problem = (await reuse.Content.ReadFromJsonAsync<PriorEvidenceReuseProblem>(ApiFactory.Json))!;
        Assert.Equal(ProblemTypes.PriorEvidenceReuse, problem.Type);
        var overlap = Assert.Single(problem.Overlaps);
        Assert.Equal(SimulationFlowKind.WinRate, overlap.FlowKind);
        Assert.Equal(10, overlap.OverlappingObservations);
        Assert.Equal(14, overlap.FitObservations);
        Assert.Equal(firstAsOf, overlap.PriorEvidence.RecordedThrough);

        // Nothing was stored.
        var runs = await GetAsync<PagedResponse<CalibrationRunDto>>($"/api/calibration-runs?workspaceId={world.WorkspaceId}");
        Assert.Equal(1, runs.TotalCount);
        Assert.Equal(setsBefore, (await GetAsync<PagedResponse<AssumptionSetDto>>(
            $"/api/assumption-sets?workspaceId={world.WorkspaceId}")).TotalCount);

        // A window after the first run's cut-off is disjoint and uses only the four new deals.
        var sequential = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(fitted.Id, Today, "Sequential update", ObservationsRecordedAfter: firstAsOf));
        Assert.Equal(firstAsOf, sequential.Run.ObservationsRecordedAfter);
        var update = sequential.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate);
        Assert.Equal(4, update.SampleSize);
        Assert.Equal((10.0, 6.0), (update.Posterior!.P1, update.Posterior.P2));

        // Its readiness has no reuse issue: the prior was stamped, the window is disjoint.
        var readiness = await ReadinessAsync(sequential.Run.Id);
        var winRate = readiness.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate);
        Assert.Equal(CalibrationReadinessStatus.Ready, winRate.Status);
        Assert.Equal(first.Run.Id, winRate.PriorEvidence!.SourceCalibrationRunId);
    }

    [Fact]
    public async Task R3_A_hand_declared_observation_prior_overlapping_the_fit_is_refused_and_a_disjoint_one_fits()
    {
        var world = await CreateMarketAsync();
        var decided = Now.AddDays(-30);
        var decidedDay = DateOnly.FromDateTime(decided.UtcDateTime);
        await DecideDealsAsync(world, wins: 6, losses: 4, decidedAt: decided);

        // The dataset-08 pattern: Beta(wins + 1, losses + 1) from the same deals the fit will read.
        var overlapping = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 7, 5),
            new PriorEvidenceDto(PriorEvidenceBasis.Observations, decidedDay.AddDays(-5), decidedDay.AddDays(5),
                Population: "Last quarter's decided deals"));
        var refused = await Client.PostAsJsonAsync("/api/calibration-runs",
            new CreateCalibrationRunRequest(overlapping.Id, Today), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var overlap = Assert.Single((await refused.Content.ReadFromJsonAsync<PriorEvidenceReuseProblem>(ApiFactory.Json))!.Overlaps);
        Assert.Equal((decidedDay, decidedDay), (overlap.OverlapFrom!.Value, overlap.OverlapThrough!.Value));
        Assert.Equal(10, overlap.OverlappingObservations);

        // Evidence from a period before these deals is disjoint.
        var disjoint = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 7, 5),
            new PriorEvidenceDto(PriorEvidenceBasis.Observations, decidedDay.AddYears(-2), decidedDay.AddYears(-1)),
            "Earlier deals");
        var fit = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(disjoint.Id, Today));
        Assert.Equal(10, fit.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate).SampleSize);

        // Malformed evidence is refused at the parameter endpoint.
        var bad = await Client.PutAsJsonAsync($"/api/assumption-sets/{disjoint.Id}/parameters",
            new UpsertParameterRequest(new DistributionSpecDto(DistributionType.Beta, 1, 1), ParameterProvenance.Asserted,
                null, world.SegmentId, SimulationFlowKind.WinRate,
                PriorEvidence: new PriorEvidenceDto(PriorEvidenceBasis.Observations)), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task R3_R6_Unknown_prior_evidence_fits_and_leaves_an_open_issue_asserted_choice_knobs_keep_the_run_exploratory()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world); // legacy-style: no prior evidence anywhere
        await DecideDealsAsync(world, wins: 6, losses: 4, decidedAt: Now.AddDays(-30));

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today));
        var readiness = await ReadinessAsync(run.Run.Id);
        var fittedSet = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{run.Run.FittedAssumptionSetId}");

        var winRate = readiness.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate);
        Assert.Equal(CalibrationReadinessStatus.NotReady, winRate.Status);
        Assert.Contains(winRate.Issues, i => i.Code == ReadinessIssueCode.PriorEvidenceUnknown);
        Assert.Null(winRate.PriorEvidence);
        Assert.Equal((10, 6, 4), (winRate.Observations, winRate.Successes!.Value, winRate.Failures!.Value));
        Assert.Contains("decided", winRate.Denominator);
        Assert.Contains("no-decision", winRate.Denominator);

        // A hazard with no events stays unfitted, with the prior's provenance and the engine's reason.
        var hazard = readiness.Parameters.Single(p => p.FlowKind == SimulationFlowKind.ChurnHazard);
        Assert.Equal(CalibrationReadinessStatus.Unfitted, hazard.Status);
        Assert.Equal(ParameterProvenance.Asserted, hazard.PriorProvenance);
        Assert.Null(hazard.Posterior);
        Assert.False(string.IsNullOrWhiteSpace(hazard.UnfittedReason));
        Assert.Contains("client-month", hazard.Units);

        // Choice-model knobs are listed as asserted, never fitted; the run is exploratory.
        Assert.Contains(readiness.Asserted, a => a.Key == ChoiceParameterKeys.SwitchingCostBase
                                                 && a.Status == CalibrationReadinessStatus.Asserted
                                                 && a.Provenance == ParameterProvenance.Asserted);
        Assert.Contains(readiness.Asserted, a => a.Key == ChoiceParameterKeys.PriceSensitivity(world.SegmentId));
        Assert.DoesNotContain(readiness.Asserted, a => a.Key.StartsWith(ChoiceParameterKeys.SalesCycleMuPrefix));
        Assert.Equal(CalibrationReadinessOverall.Exploratory, readiness.Overall);
        Assert.NotEmpty(readiness.OverallReasons);
    }

    [Fact]
    public async Task R6_Complete_independent_evidence_is_Ready_and_one_asserted_choice_knob_makes_the_run_exploratory()
    {
        var world = await CreateMarketAsync();
        var prior = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 1, 1),
            new PriorEvidenceDto(PriorEvidenceBasis.Independent));
        await DecideDealsAsync(world, wins: 6, losses: 4, decidedAt: Now.AddDays(-30));

        var ready = await ReadinessAsync((await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today))).Run.Id);
        Assert.Equal(CalibrationReadinessStatus.Ready, Assert.Single(ready.Parameters).Status);

        // A set that defines no choice-model quantity still has them: the engine's documented defaults,
        // listed as asserted with provenance Default. So even this run is exploratory.
        Assert.Equal(CalibrationReadinessOverall.Exploratory, ready.Overall);
        var defaulted = ready.Asserted.Single(a => a.Key == ChoiceParameterKeys.SwitchingCostBase);
        Assert.Equal((ParameterProvenance.Default, ChoiceParameterKeys.DefaultSwitchingCostBase),
            (defaulted.Provenance!.Value, defaulted.Distribution!.P1));
        Assert.Contains(ready.Asserted, a => a.Key == ChoiceParameterKeys.PriceSensitivity(world.SegmentId)
                                             && a.Provenance == ParameterProvenance.Default
                                             && a.Distribution!.P1 == ChoiceParameterKeys.DefaultPriceSensitivity);
        Assert.All(ready.Asserted, a => Assert.Equal(CalibrationReadinessStatus.Asserted, a.Status));

        await UpsertKnobAsync(prior.Id, ChoiceParameterKeys.SwitchingCostBase, 0.5);
        var exploratory = await ReadinessAsync((await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today))).Run.Id);
        Assert.Equal(CalibrationReadinessStatus.Ready, Assert.Single(exploratory.Parameters).Status);
        Assert.Equal(CalibrationReadinessOverall.Exploratory, exploratory.Overall);
        var asserted = exploratory.Asserted.Single(a => a.Key == ChoiceParameterKeys.SwitchingCostBase);
        Assert.Equal((ParameterProvenance.Asserted, 0.5), (asserted.Provenance!.Value, asserted.Distribution!.P1));
        Assert.Equal(ready.Asserted.Count, exploratory.Asserted.Count);
    }

    // ---------- AC2 / R4: exposure completeness ----------

    [Fact]
    public async Task AC2_Same_day_churns_count_twice_ended_relationships_stop_exposure_and_missing_acquisition_dates_are_reported()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);
        var acquired = Today.AddYears(-2);
        var ended = Today.AddYears(-1);

        var alpha = await ClientAsync(world, "Alpha", acquired, ended);
        var delta = await ClientAsync(world, "Delta", acquired, ended);
        await ClientAsync(world, "Bravo", acquired, null);
        await ClientAsync(world, "Charlie", null, null); // no acquisition date: no exposure

        foreach (var firm in new[] { alpha, delta })
            await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
                world.WorkspaceId, FactEventKind.Churn, ended, "Firm", firm,
                SimulationJson.Serialize(new ChurnFact(world.SegmentId, firm, world.OurProductId))));

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today));
        var readiness = await ReadinessAsync(run.Run.Id);

        var hazard = readiness.Parameters.Single(p => p.FlowKind == SimulationFlowKind.ChurnHazard);
        Assert.Equal(2, hazard.Successes);
        double expected = (2 * (ended.DayNumber - acquired.DayNumber) + (Today.DayNumber - acquired.DayNumber))
                          / ProductManager.Engine.Calibration.CalibrationEngine.DaysPerMonth;
        Assert.Equal(expected, hazard.Exposure!.Value, 6);
        Assert.Equal(CalibrationReadinessStatus.NotReady, hazard.Status);
        Assert.Contains(hazard.Issues, i => i.Code == ReadinessIssueCode.MissingAcquisitionDate);
        Assert.DoesNotContain(hazard.Issues, i => i.Code == ReadinessIssueCode.ChurnWithoutEndDate);

        var population = readiness.Populations.Single(p => p.ClientSegmentId == world.SegmentId);
        Assert.Equal(2, population.ChurnEvents);
        Assert.Equal(4, population.Relationships);
        Assert.Equal(1, population.RelationshipsWithoutAcquisitionDate);
        Assert.Equal(2, population.EndedRelationships);
        Assert.Equal(2, population.ActiveCustomers); // Bravo and Charlie
        Assert.Equal(expected, population.ClientMonths, 6);
    }

    [Fact]
    public async Task R4_Churn_without_an_end_date_and_logo_level_churn_at_multi_product_firms_are_open_issues()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);
        var acquired = Today.AddYears(-2);
        var firm = await ClientAsync(world, "Echo", acquired, null);
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(firm, world.OurModuleId, null, 1.0, null, acquired));

        // A churn fact that names no product, for a firm holding two of ours, whose relationships never ended.
        await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            world.WorkspaceId, FactEventKind.Churn, Today.AddMonths(-1), "Firm", firm,
            SimulationJson.Serialize(new ChurnFact(world.SegmentId, firm))));

        var readiness = await ReadinessAsync((await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today))).Run.Id);

        var hazard = readiness.Parameters.Single(p => p.FlowKind == SimulationFlowKind.ChurnHazard);
        Assert.Contains(hazard.Issues, i => i.Code == ReadinessIssueCode.ChurnWithoutEndDate);
        Assert.Contains(hazard.Issues, i => i.Code == ReadinessIssueCode.LogoLevelChurn);
        var population = readiness.Populations.Single(p => p.ClientSegmentId == world.SegmentId);
        Assert.Equal((1, 1), (population.ProductlessChurnEvents, population.MultiProductFirms));
        Assert.Equal(1, Assert.Single(population.ModuleHolders).Holders);
    }

    // ---------- AC3 / R5: observed zeros vs missing months ----------

    [Fact]
    public async Task AC3_An_observed_zero_month_contributes_exposure_and_an_unobserved_month_is_reported_missing()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        foreach (var (monthsAgo, count) in new[] { (4, 3), (3, 0), (1, 2) }) // two months ago: not observed
            await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
                world.WorkspaceId, FactEventKind.SegmentArrivalObserved, thisMonth.AddMonths(-monthsAgo),
                "ClientSegment", world.SegmentId,
                SimulationJson.Serialize(new SegmentArrivalFact(world.SegmentId, SimulationFlowKind.NewFirmArrival, count))));

        var readiness = await ReadinessAsync((await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today))).Run.Id);

        var series = readiness.ArrivalObservations.Single(a => a.FlowKind == SimulationFlowKind.NewFirmArrival);
        Assert.Equal((3, 1), (series.ObservedMonths, series.ZeroMonths));
        Assert.Equal(3.0, series.Exposure);
        Assert.Equal([thisMonth.AddMonths(-2)], series.MissingMonths);

        var arrival = readiness.Parameters.Single(p => p.FlowKind == SimulationFlowKind.NewFirmArrival);
        Assert.Equal((3, 5, 3.0), (arrival.Observations, arrival.Successes!.Value, arrival.Exposure!.Value));
        Assert.Contains(arrival.Issues, i => i.Code == ReadinessIssueCode.ArrivalMonthsMissing);
    }

    // ---------- AC4 / R4: as-of cut-off ----------

    [Fact]
    public async Task AC4_A_fit_as_of_before_a_later_recorded_outcome_excludes_it_and_explains_relationship_history()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);
        await DecideDealsAsync(world, wins: 2, losses: 1, decidedAt: Now.AddDays(-400)); // learned today

        var readiness = await ReadinessAsync((await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today.AddDays(-1)))).Run.Id);

        var winRate = readiness.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate);
        Assert.Equal(CalibrationReadinessStatus.Unfitted, winRate.Status);
        Assert.Equal(0, winRate.Observations);
        var decisions = readiness.Exclusions.Single(e => e.Kind == FactEventKind.OpportunityDecided);
        Assert.Equal(3, decisions.RecordedAfterAsOf);
        Assert.Contains(readiness.Notes, n => n.Contains("does not reconstruct", StringComparison.Ordinal));
    }

    // ---------- R7: holdout ----------

    [Fact]
    public async Task R7_Held_out_decided_deals_are_compared_with_the_posterior_and_none_is_unsupported_never_a_pass()
    {
        var world = await CreateMarketAsync();
        var prior = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 1, 1),
            new PriorEvidenceDto(PriorEvidenceBasis.Independent));
        await DecideDealsAsync(world, wins: 6, losses: 4, decidedAt: Now.AddDays(-200), recordedAt: Now.AddDays(-200));
        await DecideDealsAsync(world, wins: 5, losses: 3, decidedAt: Now.AddDays(-50)); // after the as-of date

        var asOf = Today.AddDays(-100);
        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, asOf));

        var compared = await ReadinessAsync(run.Run.Id, Today);
        Assert.Equal(asOf.AddDays(1), compared.Holdout!.HeldOutFrom);
        var winRate = Assert.Single(compared.Holdout.Comparisons);
        Assert.Equal(HoldoutComparisonStatus.Compared, winRate.Status);
        Assert.Equal((8, 5), (winRate.SampleSize, winRate.ObservedEvents!.Value));
        Assert.Equal(5.0 / 8.0, winRate.ObservedRate!.Value, 9);
        Assert.Equal(7.0 / 12.0, winRate.PosteriorMean!.Value, 9);
        Assert.True(winRate.IntervalLower < winRate.ObservedRate && winRate.ObservedRate < winRate.IntervalUpper);
        Assert.True(winRate.ObservedInsideInterval);

        var empty = await ReadinessAsync(run.Run.Id, asOf.AddDays(10));
        var none = Assert.Single(empty.Holdout!.Comparisons);
        Assert.Equal(HoldoutComparisonStatus.Unsupported, none.Status);
        Assert.Equal(0, none.SampleSize);
        Assert.Null(none.ObservedInsideInterval);
        Assert.False(string.IsNullOrWhiteSpace(none.Reason));

        var before = await Client.GetAsync($"/api/calibration-runs/{run.Run.Id}/readiness?holdoutThrough={asOf:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);
    }

    [Fact]
    public async Task R7_Held_out_arrival_months_are_compared_with_the_gamma_poisson_predictive()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world); // NewFirmArrival Point(1)
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        async Task Arrivals(int monthsAgo, int count, DateTimeOffset? recordedAt) =>
            await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
                world.WorkspaceId, FactEventKind.SegmentArrivalObserved, thisMonth.AddMonths(-monthsAgo),
                "ClientSegment", world.SegmentId,
                SimulationJson.Serialize(new SegmentArrivalFact(world.SegmentId, SimulationFlowKind.NewFirmArrival, count)),
                RecordedAt: recordedAt));
        foreach (var monthsAgo in new[] { 6, 5, 4 }) await Arrivals(monthsAgo, 2, Now.AddDays(-200));
        foreach (var monthsAgo in new[] { 2, 1 }) await Arrivals(monthsAgo, 2, null);

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, thisMonth.AddMonths(-3)));
        var arrival = (await ReadinessAsync(run.Run.Id, Today)).Holdout!.Comparisons
            .Single(c => c.FlowKind == SimulationFlowKind.NewFirmArrival);

        // Point(1) prior -> Gamma(50, 50); three months of 2 -> Gamma(56, 53).
        Assert.Equal(HoldoutComparisonStatus.Compared, arrival.Status);
        Assert.Equal((2, 4, 2.0), (arrival.SampleSize, arrival.ObservedEvents!.Value, arrival.Exposure!.Value));
        Assert.Equal(2.0, arrival.ObservedRate!.Value, 9);
        Assert.Equal(56.0 / 53.0, arrival.PosteriorMean!.Value, 9);
        // NegBin(alpha 56, p 53/55) over two months: central 90% of the count is 0..5 (see HoldoutMathTests), per month 0..2.5.
        Assert.Equal((0.0, 2.5), (arrival.IntervalLower!.Value, arrival.IntervalUpper!.Value));
        Assert.True(arrival.ObservedInsideInterval);
    }

    [Fact]
    public async Task R7_Exposure_denominated_parameters_have_no_holdout_without_relationship_history()
    {
        var world = await CreateMarketAsync();
        var prior = await CreateAssumptionSetAsync(world);
        var client = await ClientAsync(world, "Foxtrot", Today.AddYears(-3), Today.AddDays(-200));
        await PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            world.WorkspaceId, FactEventKind.Churn, Today.AddDays(-200), "Firm", client,
            SimulationJson.Serialize(new ChurnFact(world.SegmentId, client, world.OurProductId)),
            RecordedAt: Now.AddDays(-200)));

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today.AddDays(-100)));
        var holdout = (await ReadinessAsync(run.Run.Id, Today)).Holdout!;

        var churn = holdout.Comparisons.Single(c => c.FlowKind == SimulationFlowKind.ChurnHazard);
        Assert.Equal(HoldoutComparisonStatus.Unsupported, churn.Status);
        Assert.Contains("relationship", churn.Reason, StringComparison.OrdinalIgnoreCase);
    }


    // ---------- PRD8 review fixes ----------

    private static readonly PriorEvidenceDto Independent = new(PriorEvidenceBasis.Independent);

    private static DateTimeOffset At(int y, int m, int d) => new(y, m, d, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task R3_A_period_prior_keeps_its_period_rule_after_a_fit_and_its_deals_imported_later_are_refused()
    {
        var world = await CreateMarketAsync();
        var prior = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 31, 21),
            new PriorEvidenceDto(PriorEvidenceBasis.Observations, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
                Population: "2025 CRM deals"));

        // The ledger holds only 2026 Q1 deals: fit A is disjoint from the prior's 2025 period.
        await DecideDealsAsync(world, wins: 3, losses: 2, decidedAt: At(2026, 2, 15), recordedAt: At(2026, 2, 20));
        var fitA = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, new DateOnly(2026, 3, 31)));
        var fitted = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{fitA.Run.FittedAssumptionSetId}");
        var stamp = fitted.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate).PriorEvidence!;
        Assert.Equal((new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)), (stamp.ObservedFrom!.Value, stamp.ObservedThrough!.Value));
        Assert.Equal((new DateOnly(2026, 3, 31), fitA.Run.Id), (stamp.RecordedThrough!.Value, stamp.SourceCalibrationRunId!.Value));
        Assert.Contains("2026-02-15", stamp.Population);

        // Then the 2025 deals behind the prior are imported. A window after fit A's cut-off does not make them new.
        await DecideDealsAsync(world, wins: 2, losses: 1, decidedAt: At(2025, 6, 15), recordedAt: At(2026, 4, 15));
        var refused = await Client.PostAsJsonAsync("/api/calibration-runs",
            new CreateCalibrationRunRequest(fitted.Id, new DateOnly(2026, 5, 31), ObservationsRecordedAfter: new DateOnly(2026, 3, 31)),
            ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var overlap = Assert.Single((await refused.Content.ReadFromJsonAsync<PriorEvidenceReuseProblem>(ApiFactory.Json))!.Overlaps);
        Assert.Equal(PriorEvidenceRule.ObservedPeriod, overlap.Rule);
        Assert.Equal((3, 3), (overlap.OverlappingObservations, overlap.FitObservations));
        Assert.Equal(new DateOnly(2025, 6, 15), overlap.OverlapFrom);
    }

    [Fact]
    public async Task R3_An_independent_rooted_chain_of_fits_with_disjoint_windows_proceeds()
    {
        var world = await CreateMarketAsync();
        var set = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 1, 1), Independent);
        var steps = new[]
        {
            (Wins: 2, Losses: 1, Decided: At(2026, 1, 10), AsOf: new DateOnly(2026, 2, 28), After: (DateOnly?)null),
            (Wins: 1, Losses: 1, Decided: At(2026, 3, 10), AsOf: new DateOnly(2026, 4, 30), After: (DateOnly?)new DateOnly(2026, 2, 28)),
            (Wins: 1, Losses: 0, Decided: At(2026, 5, 10), AsOf: new DateOnly(2026, 6, 30), After: (DateOnly?)new DateOnly(2026, 4, 30)),
        };

        CalibrationRunResultDto? last = null;
        foreach (var step in steps)
        {
            await DecideDealsAsync(world, step.Wins, step.Losses, step.Decided, step.Decided.AddDays(5));
            last = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
                new CreateCalibrationRunRequest(set.Id, step.AsOf, ObservationsRecordedAfter: step.After));
            Assert.Equal(step.Wins + step.Losses,
                last.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate).SampleSize);
            set = await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{last.Run.FittedAssumptionSetId}");
            var stamp = set.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate).PriorEvidence!;
            Assert.Null(stamp.ObservedFrom); // nothing inherited from an Independent root
            Assert.Equal(step.AsOf, stamp.RecordedThrough);
        }

        var posterior = set.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate).Distribution;
        Assert.Equal((5.0, 3.0), (posterior.P1, posterior.P2)); // Beta(1 + 4, 1 + 2): every deal counted once
    }

    [Fact]
    public async Task R3_Upserting_without_prior_evidence_keeps_a_fit_stamp_clearing_is_explicit_and_independent_over_it_is_refused()
    {
        var world = await CreateMarketAsync();
        var prior = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 1, 1), Independent);
        await DecideDealsAsync(world, wins: 6, losses: 4, decidedAt: Now.AddDays(-30));
        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs", new CreateCalibrationRunRequest(prior.Id, Today));
        var fittedId = run.Run.FittedAssumptionSetId!.Value;
        var posterior = new DistributionSpecDto(DistributionType.Beta, 7, 5);
        UpsertParameterRequest WinRate(DistributionSpecDto d, PriorEvidenceDto? e = null, bool clear = false) =>
            new(d, ParameterProvenance.Calibrated, "edited", world.SegmentId, SimulationFlowKind.WinRate,
                PriorEvidence: e, ClearPriorEvidence: clear);
        PriorEvidenceDto? Evidence(AssumptionSetDto s) => s.Parameters.Single(p => p.FlowKind == SimulationFlowKind.WinRate).PriorEvidence;

        // An edit that says nothing about evidence (the web editor, set-param without --prior-*) keeps the stamp.
        var kept = await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{fittedId}/parameters", WinRate(posterior));
        Assert.Equal(run.Run.Id, Evidence(kept)!.SourceCalibrationRunId);

        // Relabelling the fitted posterior Independent would hide the double count: refused.
        var relabel = await Client.PutAsJsonAsync($"/api/assumption-sets/{fittedId}/parameters", WinRate(posterior, Independent), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, relabel.StatusCode);
        Assert.Equal(ProblemTypes.CorrectionRequired, (await relabel.Content.ReadFromJsonAsync<ProblemDetails>(ApiFactory.Json))!.Type);
        Assert.Equal(run.Run.Id, Evidence(await GetAsync<AssumptionSetDto>($"/api/assumption-sets/{fittedId}"))!.SourceCalibrationRunId);

        // Both at once is malformed.
        var both = await Client.PutAsJsonAsync($"/api/assumption-sets/{fittedId}/parameters", WinRate(posterior, Independent, clear: true), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

        // Clearing is deliberate, and a new distribution may carry new evidence.
        var cleared = await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{fittedId}/parameters", WinRate(posterior, clear: true));
        Assert.Null(Evidence(cleared));
        var replaced = await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{fittedId}/parameters",
            WinRate(new DistributionSpecDto(DistributionType.Beta, 1, 1), Independent));
        Assert.Equal(PriorEvidenceBasis.Independent, Evidence(replaced)!.Basis);
    }

    [Fact]
    public async Task AC5_Facts_loaded_after_the_run_that_it_would_have_read_are_an_open_issue()
    {
        var world = await CreateMarketAsync();
        var prior = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 1, 1), Independent);
        await DecideDealsAsync(world, wins: 3, losses: 2, decidedAt: Now.AddDays(-30), recordedAt: Now.AddDays(-30));
        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs", new CreateCalibrationRunRequest(prior.Id, Today.AddDays(-5)));
        Assert.Equal(CalibrationReadinessStatus.Ready, (await ReadinessAsync(run.Run.Id)).Parameters.Single().Status);

        // A backdated import after the run: recorded before its cut-off, so the stored snapshot is stale.
        await DecideDealsAsync(world, wins: 1, losses: 0, decidedAt: Now.AddDays(-20), recordedAt: Now.AddDays(-10));
        var readiness = await ReadinessAsync(run.Run.Id);
        Assert.Equal(1, readiness.Exclusions.Single(e => e.Kind == FactEventKind.OpportunityDecided).AddedAfterRun);
        var winRate = readiness.Parameters.Single();
        Assert.Equal(CalibrationReadinessStatus.NotReady, winRate.Status);
        Assert.Contains(winRate.Issues, i => i.Code == ReadinessIssueCode.LedgerChangedSinceRun);
    }

    [Fact]
    public async Task R3_A_hazard_prior_whose_period_holds_client_months_is_refused_on_exposure_alone()
    {
        var world = await CreateMarketAsync();
        var prior = await SingleParameterSetAsync(world, SimulationFlowKind.ChurnHazard, new DistributionSpecDto(DistributionType.Beta, 1, 99),
            new PriorEvidenceDto(PriorEvidenceBasis.Observations, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)));
        await ClientAsync(world, "Golf", new DateOnly(2023, 1, 1), null);
        var hotel = await ClientAsync(world, "Hotel", new DateOnly(2023, 1, 1), new DateOnly(2026, 3, 1));
        await ChurnAsync(world, hotel, new DateOnly(2026, 3, 1)); // the only churn, outside the prior's period

        var refused = await Client.PostAsJsonAsync("/api/calibration-runs", new CreateCalibrationRunRequest(prior.Id, Today), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = (await refused.Content.ReadFromJsonAsync<PriorEvidenceReuseProblem>(ApiFactory.Json))!;
        var overlap = Assert.Single(problem.Overlaps);
        Assert.Equal((0, 1), (overlap.OverlappingObservations, overlap.FitObservations));
        Assert.Equal(2 * 366 / DaysPerMonth, overlap.OverlappingExposure!.Value, 6); // two relationships through leap-year 2024
        Assert.Contains("after 2024-12-31", problem.Detail);
    }

    [Fact]
    public async Task R3_R4_The_recorded_rule_rebuilds_exposure_known_through_the_cutoff_and_a_window_starts_exposure()
    {
        var world = await CreateMarketAsync();
        var prior = await SingleParameterSetAsync(world, SimulationFlowKind.ChurnHazard, new DistributionSpecDto(DistributionType.Beta, 1, 99), Independent);
        var twoYears = Today.AddYears(-2);
        await ClientAsync(world, "India", twoYears, null);
        var juliet = await ClientAsync(world, "Juliet", twoYears, Today.AddDays(-300));
        await ChurnAsync(world, juliet, Today.AddDays(-300), Now.AddDays(-290));
        var fitA = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs", new CreateCalibrationRunRequest(prior.Id, Today.AddDays(-100)));
        var fitted = fitA.Run.FittedAssumptionSetId!.Value;

        var kilo = await ClientAsync(world, "Kilo", twoYears, Today.AddDays(-50));
        await ChurnAsync(world, kilo, Today.AddDays(-50)); // recorded now, after fit A

        // A window after Juliet's churn but before fit A's cut-off: no overlapping event, but the client-months
        // (window .. cut-off] of India and Kilo were already in fit A's denominator.
        var window = Today.AddDays(-200);
        var refused = await Client.PostAsJsonAsync("/api/calibration-runs",
            new CreateCalibrationRunRequest(fitted, Today, ObservationsRecordedAfter: window), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var overlap = Assert.Single((await refused.Content.ReadFromJsonAsync<PriorEvidenceReuseProblem>(ApiFactory.Json))!.Overlaps);
        Assert.Equal(PriorEvidenceRule.RecordedThrough, overlap.Rule);
        Assert.Equal((0, 1), (overlap.OverlappingObservations, overlap.FitObservations));
        Assert.Equal(2 * 100 / DaysPerMonth, overlap.OverlappingExposure!.Value, 6);

        // At the cut-off the window is disjoint; exposure is summed from the window start (India 100 days, Kilo 50).
        var sequential = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(fitted, Today, ObservationsRecordedAfter: Today.AddDays(-100)));
        var hazard = sequential.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.ChurnHazard);
        double exposure = 150 / DaysPerMonth;
        var priorB = fitA.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.ChurnHazard).Posterior!.P2;
        Assert.Equal(priorB + exposure - 1, hazard.Posterior!.P2, 9);
        var readiness = await ReadinessAsync(sequential.Run.Id);
        Assert.Equal(exposure, readiness.Parameters.Single().Exposure!.Value, 9);
    }

    [Fact]
    public async Task R3_A_period_prior_with_a_window_is_checked_on_effective_dates()
    {
        var world = await CreateMarketAsync();
        var prior = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 4, 3),
            new PriorEvidenceDto(PriorEvidenceBasis.Observations, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));
        await DecideDealsAsync(world, wins: 2, losses: 1, decidedAt: At(2025, 6, 1), recordedAt: At(2025, 12, 20));
        await DecideDealsAsync(world, wins: 1, losses: 1, decidedAt: At(2026, 2, 1), recordedAt: At(2026, 3, 1));
        var request = new CreateCalibrationRunRequest(prior.Id, new DateOnly(2026, 6, 30),
            ObservationsRecordedAfter: new DateOnly(2026, 1, 31));

        // The 2025 deals were recorded before the window: out of the fit, so no overlap.
        var fit = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs", request);
        Assert.Equal(2, fit.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate).SampleSize);

        // One more 2025 deal recorded inside the window is still a 2025 deal: refused.
        await DecideDealsAsync(world, wins: 1, losses: 0, decidedAt: At(2025, 7, 1), recordedAt: At(2026, 4, 1));
        var refused = await Client.PostAsJsonAsync("/api/calibration-runs", request, ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var overlap = Assert.Single((await refused.Content.ReadFromJsonAsync<PriorEvidenceReuseProblem>(ApiFactory.Json))!.Overlaps);
        Assert.Equal((1, 3), (overlap.OverlappingObservations, overlap.FitObservations));
    }

    [Fact]
    public async Task R3_A_window_on_a_hazard_periods_last_day_overlaps_by_that_day_and_the_next_day_is_disjoint()
    {
        var world = await CreateMarketAsync();
        var periodEnd = Today.AddDays(-200);
        var prior = await SingleParameterSetAsync(world, SimulationFlowKind.ChurnHazard, new DistributionSpecDto(DistributionType.Beta, 1, 99),
            new PriorEvidenceDto(PriorEvidenceBasis.Observations, periodEnd.AddYears(-1), periodEnd));
        await ClientAsync(world, "Lima", Today.AddYears(-3), null);
        var mike = await ClientAsync(world, "Mike", Today.AddYears(-3), Today.AddDays(-50));
        await ChurnAsync(world, mike, Today.AddDays(-50));

        var onTheDay = await Client.PostAsJsonAsync("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today, ObservationsRecordedAfter: periodEnd), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Conflict, onTheDay.StatusCode);
        var overlap = Assert.Single((await onTheDay.Content.ReadFromJsonAsync<PriorEvidenceReuseProblem>(ApiFactory.Json))!.Overlaps);
        Assert.Equal(2 / DaysPerMonth, overlap.OverlappingExposure!.Value, 9); // day periodEnd, two relationships

        await PostAsync<CalibrationRunResultDto>("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today, ObservationsRecordedAfter: periodEnd.AddDays(1)));
    }

    [Fact]
    public async Task R7_A_fact_the_fit_read_is_never_held_out()
    {
        var world = await CreateMarketAsync();
        var prior = await WinRateOnlySetAsync(world, new DistributionSpecDto(DistributionType.Beta, 1, 1), Independent);
        await DecideDealsAsync(world, wins: 6, losses: 4, decidedAt: Now.AddDays(-200), recordedAt: Now.AddDays(-200));
        // Effective after the as-of date but recorded before it: the fit read it.
        await DecideDealsAsync(world, wins: 1, losses: 0, decidedAt: Now.AddDays(-50), recordedAt: Now.AddDays(-150));

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs", new CreateCalibrationRunRequest(prior.Id, Today.AddDays(-100)));
        Assert.Equal(11, run.Diff.Fitted.Single(d => d.FlowKind == SimulationFlowKind.WinRate).SampleSize);

        var comparison = Assert.Single((await ReadinessAsync(run.Run.Id, Today)).Holdout!.Comparisons);
        Assert.Equal(HoldoutComparisonStatus.Unsupported, comparison.Status);
        Assert.Equal(0, comparison.SampleSize);
    }

    [Fact]
    public async Task R4_Approximated_churn_exposure_is_shown_as_the_engine_used_it()
    {
        var world = await CreateMarketAsync();
        var prior = await SingleParameterSetAsync(world, SimulationFlowKind.ChurnHazard, new DistributionSpecDto(DistributionType.Beta, 1, 99), Independent);
        var firm = await PostAsync<FirmDto>("/api/firms", new CreateFirmRequest(world.MarketId, "November", EpistemicStatus.Observed, world.SegmentId));
        var churned = Today.AddDays(-91);
        await ChurnAsync(world, firm.Id, churned); // no relationship rows at all

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs", new CreateCalibrationRunRequest(prior.Id, Today));
        var hazard = (await ReadinessAsync(run.Run.Id)).Parameters.Single();

        double expected = Math.Max(Math.Max(91 / DaysPerMonth, 1.0) / 2.0, 1.0); // churns x window / 2, at least one per churn
        Assert.Equal(expected, hazard.Exposure!.Value, 9);
        Assert.Equal(2.0, hazard.Posterior!.P1, 9);
        Assert.Equal(99 + expected - 1, hazard.Posterior.P2, 9); // the same exposure the engine divided by
        Assert.Contains(hazard.Issues, i => i.Code == ReadinessIssueCode.ExposureApproximated && i.Message.Contains("approximated"));
    }

    [Fact]
    public async Task R1_Cross_sell_no_decisions_are_excluded_not_counted_as_failures()
    {
        var world = await CreateMarketAsync();
        var prior = await SingleParameterSetAsync(world, SimulationFlowKind.CrossSellRate, new DistributionSpecDto(DistributionType.Beta, 1, 99),
            Independent, world.OurModuleId);
        var client = await ClientAsync(world, "Oscar", Today.AddYears(-1), null);
        foreach (var (stage, winner) in new[] { (OpportunityStage.Won, (Guid?)world.OurModuleId), (OpportunityStage.Won, world.RivalProductId),
                     (OpportunityStage.NoDecision, (Guid?)null) })
        {
            var opportunity = await PostAsync<OpportunityDto>("/api/opportunities",
                new CreateOpportunityRequest(client, Now.AddDays(-60), null, world.OurModuleId, [world.ReportingProcessId]));
            await PostAsync<OpportunityDto>($"/api/opportunities/{opportunity.Id}/decide",
                new DecideOpportunityRequest(stage, winner, Now.AddDays(-10)));
        }

        var run = await PostAsync<CalibrationRunResultDto>("/api/calibration-runs", new CreateCalibrationRunRequest(prior.Id, Today));
        var crossSell = (await ReadinessAsync(run.Run.Id)).Parameters.Single();
        Assert.Equal((3, 1, 1, 1), (crossSell.Observations, crossSell.Successes!.Value, crossSell.Failures!.Value, crossSell.Excluded!.Value));
        Assert.Contains("no-decision", crossSell.Denominator);
    }

    [Fact]
    public async Task R4_A_window_leaving_less_exposure_than_churns_is_a_400_that_names_the_window()
    {
        var world = await CreateMarketAsync();
        var prior = await SingleParameterSetAsync(world, SimulationFlowKind.ChurnHazard, new DistributionSpecDto(DistributionType.Beta, 1, 99), Independent);
        await ClientAsync(world, "Papa", Today.AddYears(-2), null);
        var quebec = await ClientAsync(world, "Quebec", Today.AddYears(-2), Today.AddDays(-300));
        await ChurnAsync(world, quebec, Today.AddDays(-300)); // recorded now, effective long before the window

        var response = await Client.PostAsJsonAsync("/api/calibration-runs",
            new CreateCalibrationRunRequest(prior.Id, Today, ObservationsRecordedAfter: Today.AddDays(-10)), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("observationsRecordedAfter", await response.Content.ReadAsStringAsync());
    }

    // ---------- helpers ----------

    private const double DaysPerMonth = ProductManager.Engine.Calibration.CalibrationEngine.DaysPerMonth;

    private async Task<AssumptionSetDto> SingleParameterSetAsync(MarketWorld world, SimulationFlowKind flow,
        DistributionSpecDto distribution, PriorEvidenceDto? evidence, Guid? moduleId = null)
    {
        var set = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(world.WorkspaceId, $"{flow} only"));
        return await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{set.Id}/parameters",
            new UpsertParameterRequest(distribution, ParameterProvenance.Asserted, null, world.SegmentId, flow, moduleId,
                PriorEvidence: evidence));
    }

    private Task<FactEventDto> ChurnAsync(MarketWorld world, Guid firm, DateOnly effective, DateTimeOffset? recordedAt = null) =>
        PostAsync<FactEventDto>("/api/facts", new CreateFactEventRequest(
            world.WorkspaceId, FactEventKind.Churn, effective, "Firm", firm,
            SimulationJson.Serialize(new ChurnFact(world.SegmentId, firm, world.OurProductId)), RecordedAt: recordedAt));

    private async Task<CalibrationReadinessDto> ReadinessAsync(Guid runId, DateOnly? holdoutThrough = null) =>
        await GetAsync<CalibrationReadinessDto>($"/api/calibration-runs/{runId}/readiness" +
                                                (holdoutThrough is { } h ? $"?holdoutThrough={h:yyyy-MM-dd}" : ""));

    private async Task<AssumptionSetDto> WinRateOnlySetAsync(MarketWorld world, DistributionSpecDto distribution,
        PriorEvidenceDto? evidence, string name = "Win rate only")
    {
        var set = await PostAsync<AssumptionSetDto>("/api/assumption-sets",
            new CreateAssumptionSetRequest(world.WorkspaceId, name));
        return await PutAsync<AssumptionSetDto>($"/api/assumption-sets/{set.Id}/parameters",
            new UpsertParameterRequest(distribution, ParameterProvenance.Asserted, null, world.SegmentId,
                SimulationFlowKind.WinRate, PriorEvidence: evidence));
    }

    private async Task<Guid> ClientAsync(MarketWorld world, string name, DateOnly? acquired, DateOnly? ended)
    {
        var firm = await PostAsync<FirmDto>("/api/firms",
            new CreateFirmRequest(world.MarketId, name, EpistemicStatus.Observed, world.SegmentId));
        await PostAsync<FirmProductRelationshipDto>("/api/firm-relationships",
            new CreateFirmProductRelationshipRequest(firm.Id, world.OurProductId, null, 1.0, null, acquired, null, ended));
        return firm.Id;
    }
}
