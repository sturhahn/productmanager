using System.CommandLine;

namespace ProductManager.Cli.Commands;

internal static class WorkspaceCommands
{
    public static Command Build()
    {
        var workspace = new Command("workspace", "The root aggregate. Phase 1 normally uses a single default workspace.");

        var idArg = new Argument<Guid>("id");
        var nameOption = new Option<string>("--name") { Required = true };
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists workspaces.");
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var result = await client.ListWorkspacesAsync(parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2);
            CliInfrastructure.WriteJson(result);
        }, ct));

        var get = new Command("get", "Gets a workspace by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetWorkspaceAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var getDefault = new Command("get-default", "Gets the default workspace, creating it on first run.");
        getDefault.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetDefaultWorkspaceAsync(ct2));
        }, ct));

        var create = new Command("create", "Creates a workspace.");
        create.Options.Add(nameOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.CreateWorkspaceAsync(new(parseResult.GetValue(nameOption)!), ct2));
        }, ct));

        var update = new Command("update", "Renames a workspace.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.UpdateWorkspaceAsync(parseResult.GetValue(idArg), new(parseResult.GetValue(nameOption)!), ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a workspace.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteWorkspaceAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        workspace.Subcommands.Add(list);
        workspace.Subcommands.Add(get);
        workspace.Subcommands.Add(getDefault);
        workspace.Subcommands.Add(create);
        workspace.Subcommands.Add(update);
        workspace.Subcommands.Add(delete);
        return workspace;
    }
}
