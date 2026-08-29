using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

internal static class RoadmapCommands
{
    public static Command Build()
    {
        var roadmap = new Command("roadmap", "Branchable, versioned plans. A scenario is a branch; adopting one merges it into the live roadmap.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var isLiveOption = new Option<bool?>("--is-live");
        var nameOption = new Option<string>("--name") { Required = true };
        var versionNameOption = new Option<string?>("--version-name");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists roadmaps.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(isLiveOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListRoadmapsAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(isLiveOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a roadmap by id, including its planned items.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetRoadmapAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a roadmap.");
        var createIsLiveOption = new Option<bool>("--is-live") { Description = "Make this the live roadmap immediately." };
        create.Options.Add(workspaceIdOption);
        create.Options.Add(nameOption);
        create.Options.Add(versionNameOption);
        create.Options.Add(createIsLiveOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var workspaceId = parseResult.GetValue(workspaceIdOption)
                ?? throw new ArgumentException("--workspace-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateRoadmapRequest(
                workspaceId, parseResult.GetValue(nameOption)!, parseResult.GetValue(versionNameOption),
                parseResult.GetValue(createIsLiveOption));
            CliInfrastructure.WriteJson(await client.CreateRoadmapAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Renames a roadmap / changes its version label.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(versionNameOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateRoadmapRequest(parseResult.GetValue(nameOption)!, parseResult.GetValue(versionNameOption));
            CliInfrastructure.WriteJson(await client.UpdateRoadmapAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a roadmap (the live roadmap cannot be deleted).");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteRoadmapAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        var makeLive = new Command("make-live", "Makes this roadmap the workspace's live plan, retiring whichever was live before.");
        makeLive.Arguments.Add(idArg);
        makeLive.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.MakeRoadmapLiveAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var feasibility = new Command("feasibility", "Team-capacity overallocations and dependency-ordering violations for this roadmap.");
        feasibility.Arguments.Add(idArg);
        feasibility.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetRoadmapFeasibilityAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        roadmap.Subcommands.Add(list);
        roadmap.Subcommands.Add(get);
        roadmap.Subcommands.Add(create);
        roadmap.Subcommands.Add(update);
        roadmap.Subcommands.Add(delete);
        roadmap.Subcommands.Add(makeLive);
        roadmap.Subcommands.Add(feasibility);
        roadmap.Subcommands.Add(BuildPlan(idArg));
        roadmap.Subcommands.Add(BuildReschedule(idArg));
        roadmap.Subcommands.Add(BuildUnplan(idArg));
        roadmap.Subcommands.Add(BuildReorder(idArg));
        roadmap.Subcommands.Add(BuildBranch(idArg));
        roadmap.Subcommands.Add(BuildAdopt(idArg));
        return roadmap;
    }

    private static Command BuildPlan(Argument<Guid> idArg)
    {
        var projectIdOption = new Option<Guid>("--project-id") { Required = true };
        var startOption = new Option<string>("--start") { Required = true, Description = "yyyy-MM-dd" };
        var endOption = new Option<string>("--end") { Required = true, Description = "yyyy-MM-dd" };
        var sortOrderOption = new Option<int?>("--sort-order");

        var cmd = new Command("plan", "Plans (or re-plans) a project on this roadmap.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(projectIdOption);
        cmd.Options.Add(startOption);
        cmd.Options.Add(endOption);
        cmd.Options.Add(sortOrderOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new PlanProjectRequest(
                parseResult.GetValue(projectIdOption),
                ParseHelpers.ParseDate(parseResult.GetValue(startOption)!, "--start"),
                ParseHelpers.ParseDate(parseResult.GetValue(endOption)!, "--end"),
                parseResult.GetValue(sortOrderOption));
            CliInfrastructure.WriteJson(await client.PlanProjectAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildReschedule(Argument<Guid> idArg)
    {
        var projectIdOption = new Option<Guid>("--project-id") { Required = true };
        var startOption = new Option<string>("--start") { Required = true, Description = "yyyy-MM-dd" };
        var endOption = new Option<string>("--end") { Required = true, Description = "yyyy-MM-dd" };
        var sortOrderOption = new Option<int?>("--sort-order");

        var cmd = new Command("reschedule", "Moves a planned project on the timeline (the drag-and-drop reschedule).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(projectIdOption);
        cmd.Options.Add(startOption);
        cmd.Options.Add(endOption);
        cmd.Options.Add(sortOrderOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new RescheduleItemRequest(
                ParseHelpers.ParseDate(parseResult.GetValue(startOption)!, "--start"),
                ParseHelpers.ParseDate(parseResult.GetValue(endOption)!, "--end"),
                parseResult.GetValue(sortOrderOption));
            CliInfrastructure.WriteJson(await client.RescheduleItemAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(projectIdOption), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildUnplan(Argument<Guid> idArg)
    {
        var projectIdOption = new Option<Guid>("--project-id") { Required = true };
        var cmd = new Command("unplan", "Removes a project from this roadmap.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(projectIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.UnplanProjectAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(projectIdOption), ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildReorder(Argument<Guid> idArg)
    {
        var projectIdsOption = new Option<string>("--project-ids")
        {
            Required = true,
            Description = "Comma-separated project ids in display order — must name exactly the projects planned on this roadmap."
        };
        var cmd = new Command("reorder", "Rewrites the display order (the drag-and-drop reorder).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(projectIdsOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var ids = ParseHelpers.ParseGuidList(parseResult.GetValue(projectIdsOption)) ?? [];
            CliInfrastructure.WriteJson(await client.ReorderRoadmapAsync(parseResult.GetValue(idArg), ids, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildBranch(Argument<Guid> idArg)
    {
        var branchNameOption = new Option<string>("--name") { Required = true };
        var cmd = new Command("branch", "Branches this roadmap into a scenario: a new, non-live roadmap with a deep copy of the plan.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(branchNameOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.BranchRoadmapAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(branchNameOption)!, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildAdopt(Argument<Guid> idArg)
    {
        var branchIdOption = new Option<Guid>("--branch-id") { Required = true };
        var cmd = new Command("adopt", "Adopts a scenario branch into this (live) roadmap — the merge back to main.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(branchIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.AdoptBranchAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(branchIdOption), ct2));
        }, ct));
        return cmd;
    }
}
