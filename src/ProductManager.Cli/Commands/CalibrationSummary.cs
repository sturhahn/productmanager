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

    /// <summary>
    /// Renders a readiness report (PRD8) for the terminal: the run verdict and its reasons, one row per
    /// parameter (status, N, events, exposure, posterior mean, issue codes) with every issue message and
    /// unfitted reason beneath, the asserted quantities, the holdout comparisons, and the report's notes.
    /// Populations, exclusions and arrival months stay in the JSON (<c>--output</c>).
    /// </summary>
    public static string FormatReadiness(CalibrationReadinessDto report, NameResolver? names = null)
    {
        string Label(string key) => names is null ? key : ParameterKeyTranslator.Translate(key, names.ParameterMaps);
        string Text(string text) => names is null ? text : TranslateReason(text, names.ParameterMaps);

        var sb = new StringBuilder();
        var window = report.ObservationsRecordedAfter is { } after ? $", observations recorded after {after:yyyy-MM-dd}" : "";
        sb.AppendLine($"Calibration run {report.CalibrationRunId} (as of {report.AsOfDate:yyyy-MM-dd}{window}): {report.Overall}");
        foreach (var reason in report.OverallReasons) sb.AppendLine($"  - {Text(reason)}");

        if (report.Parameters.Count > 0)
        {
            const int nameWidth = 46;
            sb.AppendLine();
            sb.AppendLine($"{Pad("Parameter", nameWidth)} {Pad("Status", 9)} {"N",6} {"Events",7} {"Exposure",9} {"Post. mean",10}  Issues");
            foreach (var p in report.Parameters)
            {
                var exposure = p.Exposure is { } e ? e.ToString("F1") : "-";
                var mean = p.PosteriorMean is { } m ? m.ToString("F4") : "-";
                var codes = string.Join(", ", p.Issues.Select(i => i.Code).Distinct());
                sb.AppendLine($"{Pad(Truncate(Label(p.Key), nameWidth), nameWidth)} {Pad(p.Status.ToString(), 9)} " +
                              $"{p.Observations,6} {(p.Successes?.ToString() ?? "-"),7} {exposure,9} {mean,10}  {codes}".TrimEnd());
            }

            foreach (var p in report.Parameters.Where(p => p.Issues.Count > 0 || p.UnfittedReason is not null))
            {
                sb.AppendLine($"  {Label(p.Key)}:");
                if (p.UnfittedReason is { } reason) sb.AppendLine($"    unfitted: {Text(reason)}");
                foreach (var issue in p.Issues) sb.AppendLine($"    {issue.Code}: {Text(issue.Message)}");
            }
        }

        if (report.Asserted.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Asserted (not fitted; not validated by this fit):");
            foreach (var a in report.Asserted)
            {
                var value = a.Distribution is { } d
                    ? $"{d.Type} {d.P1:0.####}" + (d.Type == DistributionType.Point ? "" : $", {d.P2:0.####}")
                    : $"{a.Count} row(s)";
                var provenance = a.Provenance is { } pr ? $", {pr}" : "";
                sb.AppendLine($"  {Label(a.Key)} — {a.Quantity}: {value}{provenance}");
            }
        }

        if (report.Holdout is { } holdout)
        {
            sb.AppendLine();
            sb.AppendLine($"Holdout {holdout.HeldOutFrom:yyyy-MM-dd}..{holdout.HeldOutThrough:yyyy-MM-dd} " +
                          $"(central {holdout.IntervalMass:P0} posterior-predictive interval):");
            foreach (var c in holdout.Comparisons)
            {
                var detail = c.Status == HoldoutComparisonStatus.Compared
                    ? $"n={c.SampleSize}, observed {c.ObservedRate:F4} vs posterior mean {c.PosteriorMean:F4} " +
                      $"[{c.IntervalLower:F4}, {c.IntervalUpper:F4}] — {(c.ObservedInsideInterval == true ? "inside" : "outside")}"
                    : $"n={c.SampleSize} — {Text(c.Reason ?? "")}";
                sb.AppendLine($"  {Label(c.Key)}: {c.Status}, {detail}");
            }
            sb.AppendLine($"  {holdout.Note}");
        }

        if (report.Notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Notes:");
            foreach (var note in report.Notes) sb.AppendLine($"  - {note}");
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>The evidence-reuse refusal as 'calibrate run' prints it (exit code 6): nothing was stored, the
    /// server's per-parameter overlap detail, and the two ways forward.</summary>
    public static string EvidenceReuseMessage(string? detail) =>
        "Refused: the prior already contains evidence this fit would apply again, so nothing was fitted or stored.\n" +
        (string.IsNullOrWhiteSpace(detail) ? "" : detail + "\n") +
        "Fit only newer facts with --observations-recorded-after <the prior's recordedThrough>, or declare prior " +
        "evidence that is independent of (or disjoint from) the fit's observations.";

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
