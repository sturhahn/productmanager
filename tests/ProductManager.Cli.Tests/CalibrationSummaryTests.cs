using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Tests;

public class CalibrationSummaryTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PriorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FittedId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static CalibrationParameterDiffDto Fitted(string key, double priorMean, double posteriorMean, int n) =>
        new(key, null, null, null, null, null, null, priorMean, posteriorMean, 0.05, 0.03, posteriorMean - priorMean, n, $"fitted from {n} observations");

    private static CalibrationUnfittedDto Unfitted(string key, string reason) =>
        new(key, null, null, null, null, null, reason);

    private static CalibrationDiffDto MakeDiff(IReadOnlyList<CalibrationParameterDiffDto> fitted, IReadOnlyList<CalibrationUnfittedDto> unfitted) =>
        new(RunId, new DateOnly(2026, 6, 30), PriorId, FittedId, fitted, unfitted);

    [Fact]
    public void BuildRows_orders_by_key_and_carries_every_field_through()
    {
        var diff = MakeDiff([Fitted("b-param", 0.1, 0.2, 10), Fitted("a-param", 0.5, 0.4, 25)], []);

        var rows = CalibrationSummary.BuildRows(diff);

        Assert.Equal(["a-param", "b-param"], rows.Select(r => r.Parameter));
        var a = rows[0];
        Assert.Equal(0.5, a.PriorMean);
        Assert.Equal(0.4, a.PosteriorMean);
        Assert.Equal(25, a.SampleSize);
        Assert.Equal(a.PosteriorMean - a.PriorMean, a.Shift, 10);
    }

    [Fact]
    public void FormatTable_reports_a_placeholder_when_nothing_was_fitted()
    {
        var text = CalibrationSummary.FormatTable([]);
        Assert.Contains("no parameters were fitted", text);
    }

    [Fact]
    public void FormatTable_includes_every_fitted_parameters_key_and_sample_size()
    {
        var diff = MakeDiff([Fitted("choice.switching-cost-base", 0.2, 0.31, 42)], []);
        var rows = CalibrationSummary.BuildRows(diff);

        var text = CalibrationSummary.FormatTable(rows);

        Assert.Contains("choice.switching-cost-base", text);
        Assert.Contains("42", text);
        Assert.Contains("0.2000", text);
        Assert.Contains("0.3100", text);
    }

    [Fact]
    public void FormatTable_truncates_a_very_long_key_rather_than_breaking_column_alignment()
    {
        var longKey = "utility.importance-weight:" + new string('a', 80) + ":" + new string('b', 40);
        var diff = MakeDiff([Fitted(longKey, 0, 0, 1)], []);
        var rows = CalibrationSummary.BuildRows(diff);

        var text = CalibrationSummary.FormatTable(rows);
        var firstDataLine = text.Split('\n')[1];

        Assert.Contains("…", firstDataLine);
        Assert.DoesNotContain(longKey, firstDataLine);
    }

    [Fact]
    public void FormatTable_translates_a_parameter_key_when_a_resolver_is_given()
    {
        var segmentId = Guid.Parse("cec3eedf-c83a-487f-9c3a-42d71775d02c");
        var diff = MakeDiff([Fitted($"ChurnHazard:{segmentId}", 0.1, 0.2, 10)], []);
        var rows = CalibrationSummary.BuildRows(diff);
        var names = new NameResolver(segments: new Dictionary<Guid, string> { [segmentId] = "Boutique Family Offices" });

        var text = CalibrationSummary.FormatTable(rows, names);

        Assert.Contains("Churn hazard — Boutique Family Offices", text);
        Assert.DoesNotContain(segmentId.ToString(), text);
    }

    [Fact]
    public void FormatUnfitted_translates_a_parameter_key_when_a_resolver_is_given()
    {
        var segmentId = Guid.Parse("cec3eedf-c83a-487f-9c3a-42d71775d02c");
        var diff = MakeDiff([], [Unfitted($"WinRate:{segmentId}", "no evidence")]);
        var names = new NameResolver(segments: new Dictionary<Guid, string> { [segmentId] = "Boutique Family Offices" });

        var text = CalibrationSummary.FormatUnfitted(diff, names);

        Assert.Contains("Win rate — Boutique Family Offices", text);
        Assert.Contains("no evidence", text);
    }

    [Fact]
    public void FormatUnfitted_translates_ids_embedded_in_the_reason_text()
    {
        var segmentId = Guid.Parse("cec3eedf-c83a-487f-9c3a-42d71775d02c");
        var moduleId = Guid.Parse("449ca8bb-9cef-4325-8bfe-2e6cc05776dc");
        var reason = $"No observed cross-sell wins for module '{moduleId}' in segment '{segmentId}' as of 2026-08-30.";
        var diff = MakeDiff([], [Unfitted($"CrossSellRate:{segmentId}:{moduleId}", reason)]);
        var names = new NameResolver(
            segments: new Dictionary<Guid, string> { [segmentId] = "Boutique Family Offices" },
            products: new Dictionary<Guid, string> { [moduleId] = "Meridian Trading Module" });

        var text = CalibrationSummary.FormatUnfitted(diff, names);

        Assert.Contains("module 'Meridian Trading Module' in segment 'Boutique Family Offices'", text);
        Assert.DoesNotContain(segmentId.ToString(), text);
        Assert.DoesNotContain(moduleId.ToString(), text);
    }

    [Fact]
    public void FormatUnfitted_falls_back_to_a_short_id_for_an_unresolved_embedded_guid()
    {
        var unknownId = Guid.Parse("00000000-1111-2222-3333-444444444444");
        var diff = MakeDiff([], [Unfitted("z-param", $"No data for '{unknownId}'.")]);
        var names = new NameResolver();

        var text = CalibrationSummary.FormatUnfitted(diff, names);

        Assert.DoesNotContain(unknownId.ToString(), text);
        Assert.Contains("00000000", text);
    }

    [Fact]
    public void FormatUnfitted_is_empty_when_nothing_was_unfitted()
    {
        var diff = MakeDiff([], []);
        Assert.Equal("", CalibrationSummary.FormatUnfitted(diff));
    }

    [Fact]
    public void FormatUnfitted_lists_every_unfitted_key_with_its_reason_sorted_by_key()
    {
        var diff = MakeDiff([], [Unfitted("z-param", "no evidence"), Unfitted("a-param", "invalid prior")]);

        var text = CalibrationSummary.FormatUnfitted(diff);
        var lines = text.Split('\n');

        Assert.Contains("Unfitted", lines[0]);
        Assert.Contains("a-param", lines[1]);
        Assert.Contains("invalid prior", lines[1]);
        Assert.Contains("z-param", lines[2]);
    }
}

/// <summary>PRD8: the readiness report as 'pm calibrate readiness' prints it.</summary>
public class CalibrationReadinessSummaryTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SegmentId = Guid.Parse("cec3eedf-c83a-487f-9c3a-42d71775d02c");

    private static ReadinessParameterDto Parameter(string key, SimulationFlowKind? flow, CalibrationReadinessStatus status,
        params ReadinessIssueDto[] issues) =>
        new(key, flow, SegmentId, null, null, status, "probability per decided opportunity", "decided deals",
            new DateOnly(2026, 1, 5), new DateOnly(2026, 5, 30), 10, 6, 4, 1, null,
            new DistributionSpecDto(DistributionType.Beta, 1, 1), ParameterProvenance.Asserted,
            new PriorEvidenceDto(PriorEvidenceBasis.Independent), new DistributionSpecDto(DistributionType.Beta, 7, 5),
            7.0 / 12.0, status == CalibrationReadinessStatus.Unfitted ? "No observed churn events" : null, issues);

    private static CalibrationReadinessDto Report(CalibrationReadinessOverall overall, IReadOnlyList<ReadinessParameterDto> parameters,
        IReadOnlyList<ReadinessAssertedDto>? asserted = null, ReadinessHoldoutDto? holdout = null) =>
        new(RunId, new DateOnly(2026, 6, 30), new DateOnly(2025, 12, 31), Guid.Empty, null, overall,
            overall == CalibrationReadinessOverall.Ready ? [] : ["1 asserted choice-model quantity is not validated by this fit."],
            parameters, asserted ?? [], [], [], [], ["Relationship spans ... does not reconstruct ..."], holdout);

    [Fact]
    public void EvidenceReuseMessage_says_nothing_was_stored_and_names_the_window_option()
    {
        var text = CalibrationSummary.EvidenceReuseMessage("WinRate:x: 10 of 14 observation(s) were already in the prior.");
        Assert.Contains("nothing was fitted or stored", text);
        Assert.Contains("10 of 14", text);
        Assert.Contains("--observations-recorded-after", text);
    }

    [Fact]
    public void FormatReadiness_leads_with_the_run_verdict_and_its_reasons()
    {
        var text = CalibrationSummary.FormatReadiness(Report(CalibrationReadinessOverall.Exploratory,
            [Parameter($"WinRate:{SegmentId}", SimulationFlowKind.WinRate, CalibrationReadinessStatus.Ready)]));
        var lines = text.Split('\n');

        Assert.Contains(RunId.ToString(), lines[0]);
        Assert.Contains("2026-06-30", lines[0]);
        Assert.Contains("recorded after 2025-12-31", lines[0]);
        Assert.Contains("Exploratory", lines[0]);
        Assert.Contains("not validated", text);
        Assert.Contains("does not reconstruct", text);
    }

    [Fact]
    public void FormatReadiness_prints_status_counts_and_issue_codes_per_parameter_with_translated_names()
    {
        var names = new NameResolver(segments: new Dictionary<Guid, string> { [SegmentId] = "Boutique Family Offices" });
        var text = CalibrationSummary.FormatReadiness(Report(CalibrationReadinessOverall.Exploratory,
        [
            Parameter($"WinRate:{SegmentId}", SimulationFlowKind.WinRate, CalibrationReadinessStatus.NotReady,
                new ReadinessIssueDto(ReadinessIssueCode.PriorEvidenceUnknown, "The prior carries no evidence metadata.")),
            Parameter($"ChurnHazard:{SegmentId}", SimulationFlowKind.ChurnHazard, CalibrationReadinessStatus.Unfitted)
        ]), names);

        Assert.Contains("Win rate — Boutique Family Offices", text);
        Assert.Contains("NotReady", text);
        Assert.Contains("PriorEvidenceUnknown", text);
        Assert.Contains("The prior carries no evidence metadata.", text);
        Assert.Contains("Unfitted", text);
        Assert.Contains("No observed churn events", text);
        Assert.Contains("0.5833", text);
        Assert.DoesNotContain(SegmentId.ToString(), text);
    }

    [Fact]
    public void FormatReadiness_lists_asserted_quantities_with_provenance()
    {
        var text = CalibrationSummary.FormatReadiness(Report(CalibrationReadinessOverall.Exploratory, [],
        [
            new ReadinessAssertedDto("choice.switching-cost-base", "switching cost (base)", CalibrationReadinessStatus.Asserted,
                ParameterProvenance.Asserted, new DistributionSpecDto(DistributionType.Point, 0.5), null, 1),
            new ReadinessAssertedDto("reference:integration-quality", "integration quality between products",
                CalibrationReadinessStatus.Asserted, null, null, null, 3)
        ]));

        Assert.Contains("Asserted", text);
        Assert.Contains("switching cost (base)", text);
        Assert.Contains("Point 0.5", text);
        Assert.Contains("3 row(s)", text);
    }

    [Fact]
    public void FormatReadiness_reports_holdout_comparisons_and_unsupported_ones_without_a_pass()
    {
        var holdout = new ReadinessHoldoutDto(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), 0.9, "not a pass",
        [
            new ReadinessHoldoutComparisonDto($"WinRate:{SegmentId}", SimulationFlowKind.WinRate, SegmentId, null, null,
                HoldoutComparisonStatus.Compared, null, 8, 5, null, 0.625, 7.0 / 12.0, 0.25, 0.875, true),
            new ReadinessHoldoutComparisonDto($"ChurnHazard:{SegmentId}", SimulationFlowKind.ChurnHazard, SegmentId, null, null,
                HoldoutComparisonStatus.Unsupported, "Needs client-month exposure over the held-out period.", 0, null, null,
                null, null, null, null, null)
        ]);
        var text = CalibrationSummary.FormatReadiness(Report(CalibrationReadinessOverall.Exploratory, [], holdout: holdout));

        Assert.Contains("2026-07-01..2026-09-30", text);
        Assert.Contains("n=8", text);
        Assert.Contains("0.6250", text);
        Assert.Contains("[0.2500, 0.8750]", text);
        Assert.Contains("inside", text);
        Assert.Contains("Unsupported", text);
        Assert.Contains("Needs client-month exposure", text);
        Assert.DoesNotContain("pass", text.Replace("not a pass", ""), StringComparison.OrdinalIgnoreCase);
    }
}
