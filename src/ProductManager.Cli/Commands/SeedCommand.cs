using System.CommandLine;
using System.Text.Json;
using ProductManager.Cli.Client;
using ProductManager.Cli.Seed;

namespace ProductManager.Cli.Commands;

internal static class SeedCommand
{
    public static Command Build()
    {
        var fileOption = new Option<string>("--file") { Required = true, Description = "Path to a seed-data JSON file (see seed/phase1-seed.json)." };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Check the file and print the plan (creates, updates, no-ops, exceptions) without writing anything."
        };
        var reportOption = new Option<string?>("--report")
        {
            Description = "Also write every record's planned action and actual outcome to this JSON file."
        };

        var seed = new Command("seed",
            "Loads seed data through the API. Checks the whole file first and writes nothing if it has problems; " +
            "matches existing entities (by source identity, else by name) before creating, so it is safe to re-run. " +
            "Exit code 5 means the load finished but some records are exceptions.");
        seed.Options.Add(fileOption);
        seed.Options.Add(dryRunOption);
        seed.Options.Add(reportOption);
        seed.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var path = parseResult.GetValue(fileOption)!;
            var reportPath = parseResult.GetValue(reportOption);
            if (!File.Exists(path))
                throw new ArgumentException($"Seed file not found: {path}");

            SeedFile seedFile;
            try
            {
                var json = await File.ReadAllTextAsync(path, ct2);
                seedFile = JsonSerializer.Deserialize<SeedFile>(json, ProductManagerApiClient.JsonOptions)
                    ?? throw new ArgumentException($"Seed file '{path}' is empty or not valid JSON.");
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Seed file '{path}' could not be parsed: {ex.Message}", ex);
            }

            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);

            var result = await SeedLoader.PlanAsync(client, seedFile, ct2);
            if (!parseResult.GetValue(dryRunOption) && result.Problems.Count == 0)
            {
                result = new SeedTally();
                try
                {
                    await SeedLoader.ApplyAsync(client, seedFile, result, ct2);
                }
                catch (ApiClientException ex)
                {
                    // Report what was applied before the stop, then let the usual handler set the exit code.
                    result.Failure = ex.Message;
                    Finish();
                    throw;
                }
            }

            Finish();
            if (result.Problems.Count > 0)
                throw new ArgumentException($"Seed file '{path}' failed preflight ({result.Problems.Count} problem(s) above); nothing was written.");
            if (result.HasExceptions)
                throw new ExitCodeException(5, $"Some records in '{path}' are exceptions (listed above); everything else was {(result.DryRun ? "planned" : "applied")}.");

            void Finish()
            {
                SeedReport.Print(Console.Out, path, result);
                if (reportPath is not null) SeedReport.Write(reportPath, path, result);
            }
        }, ct));

        return seed;
    }
}
