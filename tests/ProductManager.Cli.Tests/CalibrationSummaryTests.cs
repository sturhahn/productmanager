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
