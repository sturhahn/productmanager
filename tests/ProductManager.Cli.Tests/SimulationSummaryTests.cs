using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Tests;

public class SimulationSummaryTests
{
    private static EngineResult MakeResult(double npvMean, double npvP10, double npvP50, double npvP90, double[] recognizedArr, double[] bookings)
    {
        var trajectory = new EngineMetricTrajectory([0], [0], [0], [0]);
        var total = new EngineSegmentTrajectories(
            "total",
            Clients: trajectory,
            ProspectPool: trajectory,
            NewWins: trajectory,
            Churned: trajectory,
            Bookings: new EngineMetricTrajectory(bookings, bookings, bookings, bookings),
            RecognizedArr: new EngineMetricTrajectory(recognizedArr, recognizedArr, recognizedArr, recognizedArr),
            ContributionMargin: trajectory);

        return new EngineResult(
            Segments: [],
            Total: total,
            Npv: new EngineDistributionSummary(npvMean, npvP10, npvP50, npvP90),
            NpvPerPath: [],
            Paths: 100,
            Seed: 0,
            HorizonMonths: recognizedArr.Length,
            ElapsedMs: 0);
    }

    [Fact]
    public void Summarize_reads_npv_straight_from_the_distribution_summary()
    {
        var result = MakeResult(1000, 500, 900, 1600, [100, 200, 300], [10, 20, 30]);
        var headline = SimulationSummary.Summarize(result);

        Assert.Equal(1000, headline.NpvMean);
        Assert.Equal(500, headline.NpvP10);
        Assert.Equal(900, headline.NpvP50);
        Assert.Equal(1600, headline.NpvP90);
    }

    [Fact]
    public void Summarize_takes_terminal_arr_from_the_last_month_and_sums_bookings_across_the_horizon()
    {
        var result = MakeResult(0, 0, 0, 0, [100, 200, 300], [10, 20, 30]);
        var headline = SimulationSummary.Summarize(result);

        Assert.Equal(300, headline.TerminalArr);
        Assert.Equal(60, headline.CumulativeBookings);
    }

    [Fact]
    public void Summarize_does_not_throw_on_a_zero_length_horizon()
    {
        var result = MakeResult(0, 0, 0, 0, [], []);
        var headline = SimulationSummary.Summarize(result);

        Assert.Equal(0, headline.TerminalArr);
        Assert.Equal(0, headline.CumulativeBookings);
    }

    [Fact]
    public void Compare_reports_b_minus_a_for_every_headline_metric()
    {
        var a = MakeResult(1000, 500, 900, 1600, [100], [10]);
        var b = MakeResult(1500, 600, 1000, 2000, [150], [15]);

        var rows = SimulationSummary.Compare(a, b);

        var npvMean = rows.Single(r => r.Metric == "NPV mean");
        Assert.Equal(1000, npvMean.A);
        Assert.Equal(1500, npvMean.B);
        Assert.Equal(500, npvMean.Delta);
        Assert.Equal(50.0, npvMean.PercentDelta);

        var terminalArr = rows.Single(r => r.Metric == "Terminal ARR");
        Assert.Equal(50, terminalArr.Delta);

        var bookings = rows.Single(r => r.Metric == "Cumulative bookings");
        Assert.Equal(5, bookings.Delta);
    }

    [Fact]
    public void Compare_reports_a_null_percent_delta_when_the_baseline_metric_is_zero()
    {
        var a = MakeResult(0, 0, 0, 0, [0], [0]);
        var b = MakeResult(100, 0, 0, 0, [0], [0]);

        var rows = SimulationSummary.Compare(a, b);
        var npvMean = rows.Single(r => r.Metric == "NPV mean");

        Assert.Equal(100, npvMean.Delta);
        Assert.Null(npvMean.PercentDelta);
    }

    [Fact]
    public void Compare_covers_all_six_headline_metrics_exactly_once()
    {
        var a = MakeResult(1, 1, 1, 1, [1], [1]);
        var b = MakeResult(1, 1, 1, 1, [1], [1]);

        var rows = SimulationSummary.Compare(a, b);

        Assert.Equal(6, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Metric).Distinct().Count());
    }

    // ---------- FormatFunnel (Phase 3: firm-level diagnostics) ----------

    private static EngineFirmLevelDiagnostics MakeDiagnostics(
        IReadOnlyList<EngineSegmentFunnel> bySegment, IReadOnlyList<EngineChannelFunnel> byChannel) =>
        new(bySegment, byChannel, AvgSalesCycleMonths: 4.2, ExpansionCount: 3, MeanUtilityGapLostDeals: 0.15, SyntheticFirmsCreated: 2100);

    [Fact]
    public void FormatFunnel_includes_every_segment_row_and_the_scalar_diagnostics()
    {
        var diagnostics = MakeDiagnostics(
            [new EngineSegmentFunnel("seg-a", Opened: 100, Qualified: 80, Won: 30, Lost: 40, NoDecision: 10, StillOpen: 20, WinRate: 0.3)],
            []);

        var text = SimulationSummary.FormatFunnel(diagnostics);

        Assert.Contains("seg-a", text);
        Assert.Contains("100.0", text);
        Assert.Contains("4.2", text);
        Assert.Contains("Synthetic firms: 2100", text);
    }

    [Fact]
    public void FormatFunnel_omits_the_channel_section_when_there_are_no_channels()
    {
        var diagnostics = MakeDiagnostics(
            [new EngineSegmentFunnel("seg-a", 10, 8, 3, 4, 1, 2, 0.3)], []);

        var text = SimulationSummary.FormatFunnel(diagnostics);

        Assert.DoesNotContain("Channel", text);
    }

    [Fact]
    public void FormatFunnel_includes_a_channel_section_when_channels_are_present()
    {
        var diagnostics = MakeDiagnostics(
            [new EngineSegmentFunnel("seg-a", 10, 8, 3, 4, 1, 2, 0.3)],
            [new EngineChannelFunnel("direct", Opened: 10, Won: 3, WinRate: 0.3)]);

        var text = SimulationSummary.FormatFunnel(diagnostics);

        Assert.Contains("Channel", text);
        Assert.Contains("direct", text);
    }

    // ---------- FormatCompareTable ----------

    [Fact]
    public void FormatCompareTable_prints_each_runs_engine_mode_next_to_its_column_header()
    {
        var a = MakeResult(1000, 500, 900, 1600, [100], [10]);
        var b = MakeResult(1500, 600, 1000, 2000, [150], [15]);
        var rows = SimulationSummary.Compare(a, b);

        var runAId = Guid.NewGuid();
        var runBId = Guid.NewGuid();
        var text = SimulationSummary.FormatCompareTable(runAId, SimulationEngineMode.Aggregate, runBId, SimulationEngineMode.Aggregate, rows);

        Assert.Contains("A (Aggregate)", text);
        Assert.Contains("B (Aggregate)", text);
        Assert.Contains(runAId.ToString(), text);
        Assert.Contains(runBId.ToString(), text);
        Assert.Contains("NPV mean", text);
    }

    [Fact]
    public void FormatCompareTable_warns_when_the_two_runs_used_different_engine_modes()
    {
        var a = MakeResult(1000, 500, 900, 1600, [100], [10]);
        var b = MakeResult(1500, 600, 1000, 2000, [150], [15]);
        var rows = SimulationSummary.Compare(a, b);

        var text = SimulationSummary.FormatCompareTable(
            Guid.NewGuid(), SimulationEngineMode.Aggregate, Guid.NewGuid(), SimulationEngineMode.FirmLevel, rows);

        Assert.Contains("A (Aggregate)", text);
        Assert.Contains("B (FirmLevel)", text);
        Assert.Contains("WARNING", text);
        Assert.Contains("not meaningful", text);
    }

    [Fact]
    public void FormatCompareTable_omits_the_warning_when_both_runs_used_the_same_engine_mode()
    {
        var a = MakeResult(1000, 500, 900, 1600, [100], [10]);
        var b = MakeResult(1500, 600, 1000, 2000, [150], [15]);
        var rows = SimulationSummary.Compare(a, b);

        var text = SimulationSummary.FormatCompareTable(
            Guid.NewGuid(), SimulationEngineMode.FirmLevel, Guid.NewGuid(), SimulationEngineMode.FirmLevel, rows);

        Assert.DoesNotContain("WARNING", text);
    }
}
