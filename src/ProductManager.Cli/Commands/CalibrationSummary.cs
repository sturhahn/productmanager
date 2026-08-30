using System.Text;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>
/// Pure formatting over a <see cref="CalibrationDiffDto"/> — no I/O, so it is unit-testable
/// directly. Used by 'calibrate run' and 'calibrate diff' to print the prior-vs-posterior table
/// instead of (or alongside) the raw JSON.
/// </summary>
public static class CalibrationSummary
{
    public sealed record DiffRow(string Parameter, double PriorMean, double PriorStdDev, double PosteriorMean, double PosteriorStdDev, double Shift, int SampleSize);

    /// <summary>One row per fitted parameter, ordered by key so output is deterministic. Unfitted
    /// parameters (the evidence could not move them) are not rows here — see <see cref="FormatUnfitted"/>.</summary>
    public static IReadOnlyList<DiffRow> BuildRows(CalibrationDiffDto diff) =>
        diff.Fitted
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new DiffRow(f.Key, f.PriorMean, f.PriorStdDev, f.PosteriorMean, f.PosteriorStdDev, f.Shift, f.SampleSize))
            .ToList();

    /// <summary>Renders "parameter, prior mean±sd, posterior mean±sd, shift, N" as a fixed-width table.</summary>
    public static string FormatTable(IReadOnlyList<DiffRow> rows)
    {
        if (rows.Count == 0) return "(no parameters were fitted — see 'calibrate show'/'diff' for the unfitted reasons)";

        const int nameWidth = 46;
        var sb = new StringBuilder();
        sb.AppendLine($"{Pad("Parameter", nameWidth)} {Pad("Prior (mean±sd)", 20)} {Pad("Posterior (mean±sd)", 20)} {Pad("Shift", 10)} {"N",5}");

        foreach (var r in rows)
        {
            var prior = $"{r.PriorMean:F4}±{r.PriorStdDev:F4}";
            var posterior = $"{r.PosteriorMean:F4}±{r.PosteriorStdDev:F4}";
            sb.AppendLine($"{Pad(Truncate(r.Parameter, nameWidth), nameWidth)} {Pad(prior, 20)} {Pad(posterior, 20)} {r.Shift,10:F4} {r.SampleSize,5}");
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    public static string FormatUnfitted(CalibrationDiffDto diff)
    {
        if (diff.Unfitted.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("Unfitted (prior left unchanged):");
        foreach (var u in diff.Unfitted.OrderBy(u => u.Key, StringComparer.Ordinal))
            sb.AppendLine($"  {u.Key}: {u.Reason}");
        return sb.ToString().TrimEnd('\n', '\r');
    }

    private static string Pad(string s, int width) => s.Length >= width ? s : s + new string(' ', width - s.Length);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
