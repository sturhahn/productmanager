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

        var seed = new Command("seed", "Loads seed data through the API. Matches existing entities by name before creating, so it is safe to re-run.");
        seed.Options.Add(fileOption);
        seed.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var path = parseResult.GetValue(fileOption)!;
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

            SeedTally tally;
            try
            {
                tally = await SeedLoader.RunAsync(client, seedFile, ct2);
            }
            catch (SeedReferenceException ex)
            {
                throw new ArgumentException($"Seed file '{path}' has a broken reference: {ex.Message}", ex);
            }

            Console.WriteLine($"Loaded {path}:");
            foreach (var (kind, (created, matched)) in tally.Snapshot.OrderBy(kv => kv.Key))
                Console.WriteLine($"  {kind,-20} created {created,3}, matched {matched,3}");
            // Upserts have no create-vs-match story to tell; without this they do work silently.
            foreach (var (kind, applied) in tally.AppliedSnapshot.OrderBy(kv => kv.Key))
                Console.WriteLine($"  {kind,-20} applied {applied,3}");
        }, ct));

        return seed;
    }
}
