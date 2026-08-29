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
}
