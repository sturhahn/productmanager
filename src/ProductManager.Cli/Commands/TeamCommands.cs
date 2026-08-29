using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

internal static class TeamCommands
{
    public static Command Build()
    {
        var team = new Command("team", "Capacity budget units — no per-person allocation, capacity per period is a feasibility constraint.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var nameOption = new Option<string>("--name") { Required = true };
        var tagOption = new Option<string?>("--tag") { Description = "Coarse specialization tag." };
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists teams.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListTeamsAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a team by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetTeamAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a team.");
        create.Options.Add(workspaceIdOption); // required for create, optional filter for list — enforced manually below
        create.Options.Add(nameOption);
        create.Options.Add(tagOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var workspaceId = parseResult.GetValue(workspaceIdOption)
                ?? throw new ArgumentException("--workspace-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.CreateTeamAsync(
                new CreateTeamRequest(workspaceId, parseResult.GetValue(nameOption)!, parseResult.GetValue(tagOption)), ct2));
        }, ct));

        var update = new Command("update", "Renames a team / changes its tag.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(tagOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.UpdateTeamAsync(parseResult.GetValue(idArg),
                new UpdateTeamRequest(parseResult.GetValue(nameOption)!, parseResult.GetValue(tagOption)), ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a team.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteTeamAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        var setCapacity = BuildSetCapacity(idArg);

        team.Subcommands.Add(list);
        team.Subcommands.Add(get);
        team.Subcommands.Add(create);
        team.Subcommands.Add(update);
        team.Subcommands.Add(delete);
        team.Subcommands.Add(setCapacity);
        return team;
    }

    private static Command BuildSetCapacity(Argument<Guid> idArg)
    {
        var yearOption = new Option<int>("--year") { Required = true };
        var monthOption = new Option<int>("--month") { Required = true };
        var pointsOption = new Option<decimal>("--points") { Required = true, Description = "FTE-months or story points — a unit choice made in data." };

        var setCapacity = new Command("set-capacity", "Upserts the team's capacity for one period.");
        setCapacity.Arguments.Add(idArg);
        setCapacity.Options.Add(yearOption);
        setCapacity.Options.Add(monthOption);
        setCapacity.Options.Add(pointsOption);
        setCapacity.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new SetTeamCapacityRequest(
                parseResult.GetValue(yearOption), parseResult.GetValue(monthOption), parseResult.GetValue(pointsOption));
            CliInfrastructure.WriteJson(await client.SetTeamCapacityAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        return setCapacity;
    }
}
