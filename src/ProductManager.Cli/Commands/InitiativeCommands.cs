using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

internal static class InitiativeCommands
{
    public static Command Build()
    {
        var initiative = new Command("initiative", "Strategic bets — the why. Contain Projects; target market-side segments and needs.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var nameOption = new Option<string>("--name") { Required = true };
        var outcomeOption = new Option<string?>("--outcome");
        var valueOption = new Option<string?>("--hypothesized-value");
        var segmentIdsOption = new Option<string?>("--segment-ids") { Description = "Comma-separated ClientSegment ids." };
        var needIdsOption = new Option<string?>("--need-ids") { Description = "Comma-separated Need ids." };
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists initiatives.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListInitiativesAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets an initiative by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetInitiativeAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates an initiative.");
        create.Options.Add(workspaceIdOption);
        create.Options.Add(nameOption);
        create.Options.Add(outcomeOption);
        create.Options.Add(valueOption);
        create.Options.Add(segmentIdsOption);
        create.Options.Add(needIdsOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var workspaceId = parseResult.GetValue(workspaceIdOption)
                ?? throw new ArgumentException("--workspace-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateInitiativeRequest(
                workspaceId,
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(outcomeOption),
                parseResult.GetValue(valueOption),
                ParseHelpers.ParseGuidList(parseResult.GetValue(segmentIdsOption)),
                ParseHelpers.ParseGuidList(parseResult.GetValue(needIdsOption)));
            CliInfrastructure.WriteJson(await client.CreateInitiativeAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates an initiative.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(outcomeOption);
        update.Options.Add(valueOption);
        update.Options.Add(segmentIdsOption);
        update.Options.Add(needIdsOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateInitiativeRequest(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(outcomeOption),
                parseResult.GetValue(valueOption),
                ParseHelpers.ParseGuidList(parseResult.GetValue(segmentIdsOption)),
                ParseHelpers.ParseGuidList(parseResult.GetValue(needIdsOption)));
            CliInfrastructure.WriteJson(await client.UpdateInitiativeAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes an initiative.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteInitiativeAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        initiative.Subcommands.Add(list);
        initiative.Subcommands.Add(get);
        initiative.Subcommands.Add(create);
        initiative.Subcommands.Add(update);
        initiative.Subcommands.Add(delete);
        return initiative;
    }
}
