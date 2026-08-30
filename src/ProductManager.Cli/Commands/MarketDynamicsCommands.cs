using System.CommandLine;
using System.Text.Json;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>Market participants: prospects, our clients, competitors' clients, and synthetic firms.</summary>
internal static class FirmCommands
{
    public static Command Build()
    {
        var firm = new Command("firm", "Market participants — prospects, clients, and synthetic firms filling the unobserved market.");

        var idArg = new Argument<Guid>("id");
        var marketIdOption = new Option<Guid?>("--market-id");
        var segmentIdOption = new Option<Guid?>("--segment-id");
        var epistemicStatusOption = new Option<EpistemicStatus?>("--epistemic-status");
        var nameOption = new Option<string>("--name") { Required = true };
        var attributesJsonOption = new Option<string?>("--attributes-json");
        var notesOption = new Option<string?>("--notes");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists firms.");
        list.Options.Add(marketIdOption);
        list.Options.Add(segmentIdOption);
        list.Options.Add(epistemicStatusOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListFirmsAsync(
                parseResult.GetValue(marketIdOption), parseResult.GetValue(segmentIdOption), parseResult.GetValue(epistemicStatusOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a firm by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetFirmAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a firm.");
        create.Options.Add(marketIdOption);
        create.Options.Add(nameOption);
        create.Options.Add(epistemicStatusOption);
        create.Options.Add(segmentIdOption);
        create.Options.Add(attributesJsonOption);
        create.Options.Add(notesOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var marketId = parseResult.GetValue(marketIdOption) ?? throw new ArgumentException("--market-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateFirmRequest(
                marketId, parseResult.GetValue(nameOption)!,
                parseResult.GetValue(epistemicStatusOption) ?? EpistemicStatus.Observed,
                parseResult.GetValue(segmentIdOption), parseResult.GetValue(attributesJsonOption), parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.CreateFirmAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates a firm.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(epistemicStatusOption);
        update.Options.Add(segmentIdOption);
        update.Options.Add(attributesJsonOption);
        update.Options.Add(notesOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateFirmRequest(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(epistemicStatusOption) ?? EpistemicStatus.Observed,
                parseResult.GetValue(segmentIdOption), parseResult.GetValue(attributesJsonOption), parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.UpdateFirmAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a firm.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteFirmAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        firm.Subcommands.Add(list);
        firm.Subcommands.Add(get);
        firm.Subcommands.Add(create);
        firm.Subcommands.Add(update);
        firm.Subcommands.Add(delete);
        firm.Subcommands.Add(BuildImport(marketIdOption));
        firm.Subcommands.Add(BuildGenerateSynthetic());
        return firm;
    }

    private static Command BuildImport(Option<Guid?> marketIdOption)
    {
        var fileOption = new Option<string>("--file") { Required = true, Description = "Path to a JSON file containing an array of firm-import items (see FirmImportItem)." };

        var cmd = new Command("import", "Imports a roster of firms (with relationships) into one market in a single call. Matches existing firms by (market, name), so re-importing the same roster updates rather than duplicates it.");
        cmd.Options.Add(marketIdOption);
        cmd.Options.Add(fileOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var marketId = parseResult.GetValue(marketIdOption) ?? throw new ArgumentException("--market-id is required.");
            var path = parseResult.GetValue(fileOption)!;
            if (!File.Exists(path))
                throw new ArgumentException($"Firm import file not found: {path}");

            List<FirmImportItem> items;
            try
            {
                var json = await File.ReadAllTextAsync(path, ct2);
                items = JsonSerializer.Deserialize<List<FirmImportItem>>(json, ProductManagerApiClient.JsonOptions)
                    ?? throw new ArgumentException($"Firm import file '{path}' is empty or not valid JSON.");
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Firm import file '{path}' could not be parsed: {ex.Message}", ex);
            }

            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.BulkImportFirmsAsync(new BulkImportFirmsRequest(marketId, items), ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildGenerateSynthetic()
    {
        var marketIdOption = new Option<Guid>("--market") { Required = true };
        var cmd = new Command("generate-synthetic", "Fills a market's segment stocks with deterministically generated synthetic firms. Regeneration replaces the previous synthetic population.");
        cmd.Options.Add(marketIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GenerateSyntheticFirmsAsync(parseResult.GetValue(marketIdOption), ct2));
        }, ct));
        return cmd;
    }
}

/// <summary>The demand edge: what a firm — or a whole segment — must accomplish.</summary>
internal static class NeedCommands
{
    public static Command Build()
    {
        var need = new Command("need", "Demand edges — what a firm or a whole segment must accomplish, how much it matters, and the workaround cost when unmet.");

        var idArg = new Argument<Guid>("id");
        var firmIdOption = new Option<Guid?>("--firm-id");
        var segmentIdOption = new Option<Guid?>("--segment-id");
        var businessProcessIdOption = new Option<Guid?>("--business-process-id");
        var importanceOption = new Option<double>("--importance") { Required = true };
        var isHardOption = new Option<bool>("--is-hard");
        var workaroundAmountOption = new Option<decimal?>("--workaround-amount");
        var workaroundCurrencyOption = new Option<string?>("--workaround-currency") { Description = "Defaults to USD." };
        var requiredDepthJsonOption = new Option<string?>("--required-depth-json");
        var conditionalityOption = new Option<string?>("--conditionality");
        var notesOption = new Option<string?>("--notes");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists needs.");
        list.Options.Add(firmIdOption);
        list.Options.Add(segmentIdOption);
        list.Options.Add(businessProcessIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListNeedsAsync(
                parseResult.GetValue(firmIdOption), parseResult.GetValue(segmentIdOption), parseResult.GetValue(businessProcessIdOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a need by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetNeedAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a need. Exactly one of --firm-id / --segment-id must be given.");
        create.Options.Add(businessProcessIdOption);
        create.Options.Add(importanceOption);
        create.Options.Add(firmIdOption);
        create.Options.Add(segmentIdOption);
        create.Options.Add(isHardOption);
        create.Options.Add(workaroundAmountOption);
        create.Options.Add(workaroundCurrencyOption);
        create.Options.Add(requiredDepthJsonOption);
        create.Options.Add(conditionalityOption);
        create.Options.Add(notesOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var businessProcessId = parseResult.GetValue(businessProcessIdOption)
                ?? throw new ArgumentException("--business-process-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateNeedRequest(
                businessProcessId, parseResult.GetValue(importanceOption),
                parseResult.GetValue(firmIdOption), parseResult.GetValue(segmentIdOption), parseResult.GetValue(isHardOption),
                ToWorkaround(parseResult.GetValue(workaroundAmountOption), parseResult.GetValue(workaroundCurrencyOption)),
                parseResult.GetValue(requiredDepthJsonOption), parseResult.GetValue(conditionalityOption), parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.CreateNeedAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates a need.");
        update.Arguments.Add(idArg);
        update.Options.Add(importanceOption);
        update.Options.Add(isHardOption);
        update.Options.Add(workaroundAmountOption);
        update.Options.Add(workaroundCurrencyOption);
        update.Options.Add(requiredDepthJsonOption);
        update.Options.Add(conditionalityOption);
        update.Options.Add(notesOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateNeedRequest(
                parseResult.GetValue(importanceOption), parseResult.GetValue(isHardOption),
                ToWorkaround(parseResult.GetValue(workaroundAmountOption), parseResult.GetValue(workaroundCurrencyOption)),
                parseResult.GetValue(requiredDepthJsonOption), parseResult.GetValue(conditionalityOption), parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.UpdateNeedAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a need.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteNeedAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        need.Subcommands.Add(list);
        need.Subcommands.Add(get);
        need.Subcommands.Add(create);
        need.Subcommands.Add(update);
        need.Subcommands.Add(delete);
        return need;
    }

    private static MoneyDto? ToWorkaround(decimal? amount, string? currency) =>
        amount is null ? null : new MoneyDto(amount.Value, currency ?? "USD");
}

/// <summary>Typed shocks that modulate simulated flows for a duration — real or hypothetical.</summary>
internal static class MarketEventCommands
{
    public static Command Build()
    {
        var marketEvent = new Command("market-event", "Typed shocks that modulate simulated flows for a duration — real events mirror the facts ledger; hypothetical ones are injected into a scenario.");

        var idArg = new Argument<Guid>("id");
        var marketIdOption = new Option<Guid?>("--market-id");
        var isHypotheticalOption = new Option<bool?>("--is-hypothetical");
        var competitorIdOption = new Option<Guid?>("--competitor-id");
        var typeOption = new Option<string>("--type") { Required = true, Description = "ProductionIncident | ForcedMigration | PricingModelChange | RegulatoryChange | FeatureLaunch | Other" };
        var nameOption = new Option<string>("--name") { Required = true };
        var startOption = new Option<string>("--start") { Required = true, Description = "yyyy-MM-dd" };
        var endOption = new Option<string?>("--end") { Description = "yyyy-MM-dd" };
        var magnitudeOption = new Option<double?>("--magnitude");
        var notesOption = new Option<string?>("--notes");
        var rateMultipliersOption = new Option<string?>("--rate-multipliers")
        {
            Description = "Comma-separated 'flowKind:multiplier' or 'segmentId/flowKind:multiplier' entries, " +
                           "e.g. \"WinRate:1.3,ChurnHazard:0.8\" or \"11111111-.../WinRate:1.3\". Omit segment id to apply to all segments."
        };
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists market events.");
        list.Options.Add(marketIdOption);
        list.Options.Add(isHypotheticalOption);
        list.Options.Add(competitorIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListMarketEventsAsync(
                parseResult.GetValue(marketIdOption), parseResult.GetValue(isHypotheticalOption), parseResult.GetValue(competitorIdOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a market event by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetMarketEventAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a market event.");
        create.Options.Add(marketIdOption);
        create.Options.Add(typeOption);
        create.Options.Add(nameOption);
        create.Options.Add(startOption);
        create.Options.Add(endOption);
        create.Options.Add(isHypotheticalOption);
        create.Options.Add(magnitudeOption);
        create.Options.Add(competitorIdOption);
        create.Options.Add(notesOption);
        create.Options.Add(rateMultipliersOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var marketId = parseResult.GetValue(marketIdOption) ?? throw new ArgumentException("--market-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateMarketEventRequest(
                marketId,
                ParseHelpers.ParseEnum<MarketEventType>(parseResult.GetValue(typeOption)!, "--type"),
                parseResult.GetValue(nameOption)!,
                ParseHelpers.ParseDate(parseResult.GetValue(startOption)!, "--start"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(endOption), "--end"),
                parseResult.GetValue(isHypotheticalOption) ?? false,
                parseResult.GetValue(magnitudeOption),
                parseResult.GetValue(competitorIdOption),
                parseResult.GetValue(notesOption),
                ParseRateMultipliers(parseResult.GetValue(rateMultipliersOption)));
            CliInfrastructure.WriteJson(await client.CreateMarketEventAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates a market event.");
        update.Arguments.Add(idArg);
        update.Options.Add(typeOption);
        update.Options.Add(nameOption);
        update.Options.Add(startOption);
        update.Options.Add(endOption);
        update.Options.Add(isHypotheticalOption);
        update.Options.Add(magnitudeOption);
        update.Options.Add(competitorIdOption);
        update.Options.Add(notesOption);
        update.Options.Add(rateMultipliersOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateMarketEventRequest(
                ParseHelpers.ParseEnum<MarketEventType>(parseResult.GetValue(typeOption)!, "--type"),
                parseResult.GetValue(nameOption)!,
                ParseHelpers.ParseDate(parseResult.GetValue(startOption)!, "--start"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(endOption), "--end"),
                parseResult.GetValue(isHypotheticalOption) ?? false,
                parseResult.GetValue(magnitudeOption),
                parseResult.GetValue(competitorIdOption),
                parseResult.GetValue(notesOption),
                ParseRateMultipliers(parseResult.GetValue(rateMultipliersOption)));
            CliInfrastructure.WriteJson(await client.UpdateMarketEventAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a market event.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteMarketEventAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        marketEvent.Subcommands.Add(list);
        marketEvent.Subcommands.Add(get);
        marketEvent.Subcommands.Add(create);
        marketEvent.Subcommands.Add(update);
        marketEvent.Subcommands.Add(delete);
        return marketEvent;
    }

    /// <summary>Parses "--rate-multipliers" entries of the form "flowKind:multiplier" or
    /// "segmentId/flowKind:multiplier" (segment omitted = applies to all segments).</summary>
    internal static IReadOnlyList<EventRateMultiplierDto>? ParseRateMultipliers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var result = new List<EventRateMultiplierDto>();
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = token.LastIndexOf(':');
            if (colon < 0)
                throw new ArgumentException($"'{token}' is not a valid --rate-multipliers entry (expected 'flowKind:multiplier' or 'segmentId/flowKind:multiplier').");

            var lhs = token[..colon];
            var multiplierText = token[(colon + 1)..];
            if (!double.TryParse(multiplierText, System.Globalization.CultureInfo.InvariantCulture, out var multiplier))
                throw new ArgumentException($"'{multiplierText}' is not a valid multiplier in --rate-multipliers entry '{token}'.");

            var slash = lhs.IndexOf('/');
            Guid? segmentId = null;
            var flowKindText = lhs;
            if (slash >= 0)
            {
                segmentId = ParseHelpers.ParseGuid(lhs[..slash], "--rate-multipliers segment id");
                flowKindText = lhs[(slash + 1)..];
            }

            var flowKind = ParseHelpers.ParseEnum<SimulationFlowKind>(flowKindText, "--rate-multipliers flow kind");
            result.Add(new EventRateMultiplierDto(segmentId, flowKind, multiplier));
        }

        return result;
    }
}
