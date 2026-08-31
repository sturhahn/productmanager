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

    /// <summary>Renders 'sim compare's delta table for the console: one row per headline metric, with
    /// each run's <see cref="SimulationEngineMode"/> printed next to its own column header (Aggregate's
    /// NPV and FirmLevel's NPV are not the same kind of number, so the reader needs to see which is
    /// which at a glance). When the two runs were executed in different modes, a warning line is
    /// appended calling out that a magnitude comparison between them is not meaningful — the raw
    /// numbers are still shown (an analyst may still want the diff), but flagged.</summary>
    public static string FormatCompareTable(
        Guid runAId, SimulationEngineMode modeA, Guid runBId, SimulationEngineMode modeB, IReadOnlyList<MetricDelta> rows)
    {
        var headerA = $"A ({modeA})";
        var headerB = $"B ({modeB})";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Run A: {runAId} [{modeA}]");
        sb.AppendLine($"Run B: {runBId} [{modeB}]");
        sb.AppendLine();
        sb.AppendLine($"{"Metric",-22} {headerA,18} {headerB,18} {"Delta (B-A)",14} {"% Delta",10}");
        foreach (var r in rows)
        {
            var percent = r.PercentDelta is null ? "n/a" : $"{r.PercentDelta:F1}%";
            sb.AppendLine($"{r.Metric,-22} {r.A,18:F1} {r.B,18:F1} {r.Delta,14:F1} {percent,10}");
        }

        if (modeA != modeB)
        {
            sb.AppendLine();
            sb.AppendLine($"WARNING: run A ran in {modeA} mode and run B ran in {modeB} mode — cross-mode " +
                           "magnitude comparisons are not meaningful (the two engines model different mechanics).");
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>Renders a firm-level run's opportunity funnels (by segment, then by channel) plus the
    /// scalar diagnostics — what 'sim run --mode firm-level' prints instead of raw JSON. Segment and
    /// channel ids are translated to names via <paramref name="names"/> when given (falling back to
    /// a short-guid form on a lookup miss); omit it (or pass <see cref="NameResolver.Empty"/>) to
    /// print the raw ids, as the existing tests do.</summary>
    public static string FormatFunnel(EngineFirmLevelDiagnostics diagnostics, NameResolver? names = null)
    {
        names ??= NameResolver.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{"Segment",-38} {"Opened",8} {"Qualified",10} {"Won",8} {"Lost",8} {"NoDec",8} {"Open",8} {"WinRate",8}");
        foreach (var f in diagnostics.FunnelBySegment)
            sb.AppendLine($"{Truncate(names.Segment(f.SegmentId)),-38} {f.Opened,8:F1} {f.Qualified,10:F1} {f.Won,8:F1} {f.Lost,8:F1} {f.NoDecision,8:F1} {f.StillOpen,8:F1} {f.WinRate,8:P1}");

        if (diagnostics.FunnelByChannel.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{"Channel",-38} {"Opened",8} {"Won",8} {"WinRate",8}");
            foreach (var c in diagnostics.FunnelByChannel)
                sb.AppendLine($"{Truncate(names.Channel(c.ChannelId)),-38} {c.Opened,8:F1} {c.Won,8:F1} {c.WinRate,8:P1}");
        }

        sb.AppendLine();
        sb.AppendLine($"Avg sales cycle: {diagnostics.AvgSalesCycleMonths:F1} months   " +
                       $"Expansions: {diagnostics.ExpansionCount:F1}   " +
                       $"Mean utility gap (lost deals): {diagnostics.MeanUtilityGapLostDeals:F3}   " +
                       $"Synthetic firms: {diagnostics.SyntheticFirmsCreated:F0}");

        return sb.ToString().TrimEnd('\n', '\r');
    }

    private static string Truncate(string s) => s.Length <= 38 ? s : s[..37] + "…";
}
