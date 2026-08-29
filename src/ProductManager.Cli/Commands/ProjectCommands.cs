using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

internal static class ProjectCommands
{
    public static Command Build()
    {
        var project = new Command("project", "Units of execution: dates, per-team effort demand, dependencies, and delivered features.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var initiativeIdOption = new Option<Guid?>("--initiative-id");
        var nameOption = new Option<string>("--name") { Required = true };
        var descriptionOption = new Option<string?>("--description");
        var statusOption = new Option<string?>("--status") { Description = "Proposed | Planned | InProgress | Done | Cancelled" };
        var startOption = new Option<string?>("--start") { Description = "yyyy-MM-dd" };
        var endOption = new Option<string?>("--end") { Description = "yyyy-MM-dd" };
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists projects.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(initiativeIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListProjectsAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(initiativeIdOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a project by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetProjectAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a project under an initiative.");
        create.Options.Add(initiativeIdOption);
        create.Options.Add(nameOption);
        create.Options.Add(descriptionOption);
        create.Options.Add(statusOption);
        create.Options.Add(startOption);
        create.Options.Add(endOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var initiativeId = parseResult.GetValue(initiativeIdOption)
                ?? throw new ArgumentException("--initiative-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateProjectRequest(
                initiativeId,
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(descriptionOption),
                ParseHelpers.ParseOptionalEnum<ProjectStatus>(parseResult.GetValue(statusOption), "--status"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(startOption), "--start"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(endOption), "--end"));
            CliInfrastructure.WriteJson(await client.CreateProjectAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates a project.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(descriptionOption);
        update.Options.Add(statusOption);
        update.Options.Add(startOption);
        update.Options.Add(endOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateProjectRequest(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(descriptionOption),
                ParseHelpers.ParseOptionalEnum<ProjectStatus>(parseResult.GetValue(statusOption), "--status"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(startOption), "--start"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(endOption), "--end"));
            CliInfrastructure.WriteJson(await client.UpdateProjectAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a project.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteProjectAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        project.Subcommands.Add(list);
        project.Subcommands.Add(get);
        project.Subcommands.Add(create);
        project.Subcommands.Add(update);
        project.Subcommands.Add(delete);
        project.Subcommands.Add(BuildAddDependency(idArg));
        project.Subcommands.Add(BuildRemoveDependency(idArg));
        project.Subcommands.Add(BuildSetEffortDemand(idArg));
        project.Subcommands.Add(BuildLinkFeature(idArg));
        project.Subcommands.Add(BuildUnlinkFeature(idArg));
        return project;
    }

    private static Command BuildAddDependency(Argument<Guid> idArg)
    {
        var dependsOnOption = new Option<Guid>("--depends-on") { Required = true };
        var cmd = new Command("add-dependency", "Declares that this project cannot start before another project finishes.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(dependsOnOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.AddProjectDependencyAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(dependsOnOption), ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildRemoveDependency(Argument<Guid> idArg)
    {
        var dependsOnOption = new Option<Guid>("--depends-on") { Required = true };
        var cmd = new Command("remove-dependency", "Removes a dependency edge.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(dependsOnOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.RemoveProjectDependencyAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(dependsOnOption), ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildSetEffortDemand(Argument<Guid> idArg)
    {
        var teamIdOption = new Option<Guid>("--team-id") { Required = true };
        var yearOption = new Option<int>("--year") { Required = true };
        var monthOption = new Option<int>("--month") { Required = true };
        var pointsOption = new Option<decimal>("--points") { Required = true, Description = "0 clears the demand." };
        var cmd = new Command("set-effort-demand", "Upserts the effort this project demands from a team in a period.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(teamIdOption);
        cmd.Options.Add(yearOption);
        cmd.Options.Add(monthOption);
        cmd.Options.Add(pointsOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new SetEffortDemandRequest(
                parseResult.GetValue(teamIdOption), parseResult.GetValue(yearOption),
                parseResult.GetValue(monthOption), parseResult.GetValue(pointsOption));
            CliInfrastructure.WriteJson(await client.SetEffortDemandAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildLinkFeature(Argument<Guid> idArg)
    {
        var featureIdOption = new Option<Guid>("--feature-id") { Required = true };
        var cmd = new Command("link-feature", "Links a feature this project delivers — the seam between execution and market sides.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(featureIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.LinkFeatureAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(featureIdOption), ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildUnlinkFeature(Argument<Guid> idArg)
    {
        var featureIdOption = new Option<Guid>("--feature-id") { Required = true };
        var cmd = new Command("unlink-feature", "Unlinks a delivered feature.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(featureIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.UnlinkFeatureAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(featureIdOption), ct2));
        }, ct));
        return cmd;
    }
}
