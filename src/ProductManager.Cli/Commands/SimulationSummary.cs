using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>
/// Pure headline-metric math over an <see cref="EngineResult"/> — no I/O, so it is unit-testable
/// directly. Used by 'sim run' (to print a digest instead of the raw trajectory JSON) and
/// 'sim compare' (to build the side-by-side delta table).
/// </summary>
public static class SimulationSummary
{
    public sealed record Headline(double NpvMean, double NpvP10, double NpvP50, double NpvP90, double TerminalArr, double CumulativeBookings);

    public static Headline Summarize(EngineResult result) => new(
        result.Npv.Mean,
        result.Npv.P10,
        result.Npv.P50,
        result.Npv.P90,
        LastOrZero(result.Total.RecognizedArr.Mean),
        result.Total.Bookings.Mean.Sum());

    private static double LastOrZero(double[] series) => series.Length == 0 ? 0 : series[^1];

    public sealed record MetricDelta(string Metric, double A, double B, double Delta, double? PercentDelta);

    /// <summary>Builds the 'sim compare' delta table: one row per headline metric, B minus A.
    /// PercentDelta is null when A is zero (a percentage change from zero is undefined).</summary>
    public static IReadOnlyList<MetricDelta> Compare(EngineResult a, EngineResult b)
    {
        var ha = Summarize(a);
        var hb = Summarize(b);

        return
        [
            Row("NPV mean", ha.NpvMean, hb.NpvMean),
            Row("NPV P10", ha.NpvP10, hb.NpvP10),
            Row("NPV P50", ha.NpvP50, hb.NpvP50),
            Row("NPV P90", ha.NpvP90, hb.NpvP90),
            Row("Terminal ARR", ha.TerminalArr, hb.TerminalArr),
            Row("Cumulative bookings", ha.CumulativeBookings, hb.CumulativeBookings)
        ];
    }

    private static MetricDelta Row(string metric, double a, double b)
    {
        var delta = b - a;
        double? percent = a == 0 ? null : delta / Math.Abs(a) * 100.0;
        return new MetricDelta(metric, a, b, delta, percent);
    }
}
