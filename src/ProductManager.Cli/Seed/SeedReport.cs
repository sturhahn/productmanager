using System.Text.Json;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Seed;

/// <summary>The two views of a seed pass: the printed summary and the <c>--report</c> JSON file.</summary>
public static class SeedReport
{
    /// <summary>Stated in every report, because an agent resuming a failed load needs to know it.</summary>
    public const string Boundary =
        "Records are applied one by one in dependency order; there is no transaction across records or datasets. " +
        "Every write is repeat-safe, so a run that stopped midway resumes by running the same file again. " +
        "An import advances records but never rewrites history: a changed decision, a deal moved to another firm " +
        "or a restated fact that differs is a correction-required exception, left as stored.";

    private static readonly JsonSerializerOptions Json = new(ProductManagerApiClient.JsonOptions) { WriteIndented = true };

    public static void Write(string path, string file, SeedTally tally) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            file,
            dryRun = tally.DryRun,
            boundary = Boundary,
            problems = tally.Problems,
            failure = tally.Failure,
            records = tally.Records
        }, Json));

    public static void Print(TextWriter output, string file, SeedTally tally)
    {
        output.WriteLine(tally.DryRun ? $"Plan for {file} (dry run, nothing written):" : $"Loaded {file}:");
        foreach (var (kind, (created, matched)) in tally.Snapshot.OrderBy(kv => kv.Key))
            output.WriteLine($"  {kind,-20} created {created,3}, matched {matched,3}");
        // Upserts have no create-vs-match story to tell; without this they do work silently.
        foreach (var (kind, applied) in tally.AppliedSnapshot.OrderBy(kv => kv.Key))
            output.WriteLine($"  {kind,-20} applied {applied,3}");
        foreach (var (kind, (updated, unchanged, exceptions)) in tally.SourcedSnapshot.OrderBy(kv => kv.Key))
            output.WriteLine($"  {kind,-20} updated {updated,3}, unchanged {unchanged,3}, exceptions {exceptions,3}");

        foreach (var record in tally.Records.Where(r => r.Planned == SeedAction.Exception || r.Outcome == SeedAction.Exception))
            output.WriteLine($"  EXCEPTION {record.Kind} {record.Key}{Source(record)}: {record.Reason}");
        foreach (var record in tally.Records.Where(r => r.Conflicts is not null))
        foreach (var conflict in record.Conflicts!)
            output.WriteLine($"  CONFLICT  {record.Kind} {record.Key}: '{conflict.Field}' was edited locally; kept " +
                             $"'{conflict.StoredValue}', not applied '{conflict.IncomingValue}'.");

        if (tally.Problems.Count > 0)
            output.WriteLine(SeedLoader.DescribeProblems(tally.Problems));
        if (tally.Failure is not null)
            output.WriteLine($"  STOPPED: {tally.Failure} Fix the cause and run the same file again to resume.");
    }

    private static string Source(SeedRecordOutcome record) =>
        record.Source is null ? "" : $" ({SeedPreflight.Describe(record.Source)})";
}
