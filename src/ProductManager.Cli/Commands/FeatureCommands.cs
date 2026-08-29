using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

internal static class FeatureCommands
{
    public static Command Build()
    {
        var feature = new Command("feature", "Product capabilities (not work items) — market meaning is a set of deltas.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var productIdOption = new Option<Guid?>("--product-id");
        var nameOption = new Option<string>("--name") { Required = true };
        var descriptionOption = new Option<string?>("--description");
        var statusOption = new Option<string?>("--status") { Description = "Hypothetical | Planned | Shipped" };
        var shippedDateOption = new Option<string?>("--shipped-date") { Description = "yyyy-MM-dd, required if --status Shipped" };
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists features.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(productIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListFeaturesAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(productIdOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a feature by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetFeatureAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a feature.");
        create.Options.Add(workspaceIdOption);
        create.Options.Add(nameOption);
        create.Options.Add(descriptionOption);
        create.Options.Add(productIdOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var workspaceId = parseResult.GetValue(workspaceIdOption)
                ?? throw new ArgumentException("--workspace-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateFeatureRequest(
                workspaceId, parseResult.GetValue(nameOption)!, parseResult.GetValue(descriptionOption),
                parseResult.GetValue(productIdOption));
            CliInfrastructure.WriteJson(await client.CreateFeatureAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates a feature.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(descriptionOption);
        update.Options.Add(productIdOption);
        update.Options.Add(statusOption);
        update.Options.Add(shippedDateOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateFeatureRequest(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(descriptionOption),
                parseResult.GetValue(productIdOption),
                ParseHelpers.ParseOptionalEnum<FeatureStatus>(parseResult.GetValue(statusOption), "--status"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(shippedDateOption), "--shipped-date"));
            CliInfrastructure.WriteJson(await client.UpdateFeatureAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a feature.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteFeatureAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        feature.Subcommands.Add(list);
        feature.Subcommands.Add(get);
        feature.Subcommands.Add(create);
        feature.Subcommands.Add(update);
        feature.Subcommands.Add(delete);
        feature.Subcommands.Add(BuildAddDelta(idArg));
        return feature;
    }

    private static Command BuildAddDelta(Argument<Guid> idArg)
    {
        var kindOption = new Option<string>("--kind") { Required = true, Description = "Coverage | SwitchingCost | Other" };
        var businessProcessIdOption = new Option<Guid?>("--business-process-id");
        var fromTierOption = new Option<string?>("--from-tier") { Description = "NotPlaying | Bronze | Silver | Gold" };
        var toTierOption = new Option<string?>("--to-tier") { Description = "NotPlaying | Bronze | Silver | Gold" };
        var approachIdOption = new Option<Guid?>("--approach-id");
        var segmentIdOption = new Option<Guid?>("--segment-id");
        var magnitudeOption = new Option<double?>("--magnitude");
        var rationaleOption = new Option<string?>("--rationale");

        var cmd = new Command("add-delta", "Adds one delta (coverage or other) to the feature's market meaning.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(kindOption);
        cmd.Options.Add(businessProcessIdOption);
        cmd.Options.Add(fromTierOption);
        cmd.Options.Add(toTierOption);
        cmd.Options.Add(approachIdOption);
        cmd.Options.Add(segmentIdOption);
        cmd.Options.Add(magnitudeOption);
        cmd.Options.Add(rationaleOption);

        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new AddFeatureDeltaRequest(
                ParseHelpers.ParseEnum<FeatureDeltaKind>(parseResult.GetValue(kindOption)!, "--kind"),
                parseResult.GetValue(businessProcessIdOption),
                ParseHelpers.ParseOptionalEnum<CoverageTier>(parseResult.GetValue(fromTierOption), "--from-tier"),
                ParseHelpers.ParseOptionalEnum<CoverageTier>(parseResult.GetValue(toTierOption), "--to-tier"),
                parseResult.GetValue(approachIdOption),
                parseResult.GetValue(segmentIdOption),
                parseResult.GetValue(magnitudeOption),
                parseResult.GetValue(rationaleOption));
            CliInfrastructure.WriteJson(await client.AddFeatureDeltaAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        return cmd;
    }
}
