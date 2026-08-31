using System.Text;
using System.Text.RegularExpressions;
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

    /// <summary>Renders "parameter, prior mean±sd, posterior mean±sd, shift, N" as a fixed-width
    /// table. Pass <paramref name="names"/> to translate each parameter key into a display name
    /// (e.g. "ChurnHazard:{guid}" -&gt; "Churn hazard — Boutique Family Offices") via
    /// <see cref="ParameterKeyTranslator"/>; omit it to print the raw key, as the existing tests do.</summary>
    public static string FormatTable(IReadOnlyList<DiffRow> rows, NameResolver? names = null)
    {
        if (rows.Count == 0) return "(no parameters were fitted — see 'calibrate show'/'diff' for the unfitted reasons)";

        const int nameWidth = 46;
        var sb = new StringBuilder();
        sb.AppendLine($"{Pad("Parameter", nameWidth)} {Pad("Prior (mean±sd)", 20)} {Pad("Posterior (mean±sd)", 20)} {Pad("Shift", 10)} {"N",5}");

        foreach (var r in rows)
        {
            var label = names is null ? r.Parameter : ParameterKeyTranslator.Translate(r.Parameter, names.ParameterMaps);
            var prior = $"{r.PriorMean:F4}±{r.PriorStdDev:F4}";
            var posterior = $"{r.PosteriorMean:F4}±{r.PosteriorStdDev:F4}";
            sb.AppendLine($"{Pad(Truncate(label, nameWidth), nameWidth)} {Pad(prior, 20)} {Pad(posterior, 20)} {r.Shift,10:F4} {r.SampleSize,5}");
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>Same key translation as <see cref="FormatTable"/>, applied to the unfitted list.
    /// The engine's <c>Reason</c> text is free-form prose that itself embeds raw segment/module ids
    /// (the engine is a pure library with no name lookup of its own — see
    /// <c>ProductManager.Engine.Calibration.CalibrationEngine</c>) — <see cref="TranslateReason"/>
    /// resolves those too, so no raw Guid ever reaches the terminal.</summary>
    public static string FormatUnfitted(CalibrationDiffDto diff, NameResolver? names = null)
    {
        if (diff.Unfitted.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("Unfitted (prior left unchanged):");
        foreach (var u in diff.Unfitted.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            var label = names is null ? u.Key : ParameterKeyTranslator.Translate(u.Key, names.ParameterMaps);
            var reason = names is null ? u.Reason : TranslateReason(u.Reason, names.ParameterMaps);
            sb.AppendLine($"  {label}: {reason}");
        }
        return sb.ToString().TrimEnd('\n', '\r');
    }

    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    /// <summary>Replaces every Guid-shaped substring of free-form engine reason text with its
    /// resolved name (segment, then product/module, then business process), falling back to
    /// <see cref="GuidDisplay"/>'s short-id form on a lookup miss — the same fallback
    /// <see cref="ParameterKeyTranslator"/> uses, so an unresolvable id never prints as a raw
    /// 36-character Guid.</summary>
    private static string TranslateReason(string reason, ParameterNameMaps names) =>
        GuidPattern.Replace(reason, m =>
        {
            var id = Guid.Parse(m.Value);
            if (names.Segments.TryGetValue(id, out var segment)) return segment;
            if (names.Products.TryGetValue(id, out var product)) return product;
            if (names.BusinessProcesses.TryGetValue(id, out var process)) return process;
            return GuidDisplay.Resolve(EmptyGuidMap, id);
        });

    private static readonly IReadOnlyDictionary<Guid, string> EmptyGuidMap = new Dictionary<Guid, string>();

    private static string Pad(string s, int width) => s.Length >= width ? s : s + new string(' ', width - s.Length);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
