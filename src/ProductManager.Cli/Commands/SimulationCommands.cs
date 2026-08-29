using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>Immutable simulation executions — run, inspect, and compare Monte Carlo forecasts.</summary>
internal static class SimulationCommands
{
    public static Command Build()
    {
        var sim = new Command("sim", "Simulation runs — execute the engine against a roadmap + assumption set, and inspect or compare results.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var roadmapIdOption = new Option<Guid?>("--roadmap-id");
        var assumptionSetIdOption = new Option<Guid?>("--assumption-set-id");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists simulation runs.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(roadmapIdOption);
        list.Options.Add(assumptionSetIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListSimulationRunsAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(roadmapIdOption), parseResult.GetValue(assumptionSetIdOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var show = new Command("show", "Shows a run's metadata (parameters it was executed with, not its trajectories).");
        show.Arguments.Add(idArg);
        show.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetSimulationRunAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var result = new Command("result", "The run's full trajectories: per-segment and total bands (mean/P10/P50/P90) plus the NPV distribution.");
        result.Arguments.Add(idArg);
        result.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetSimulationRunResultAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var input = new Command("input", "The exact engine input snapshot this run was executed against — re-running the engine on this alone reproduces the result.");
        input.Arguments.Add(idArg);
        input.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetSimulationRunInputAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a run.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteSimulationRunAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        sim.Subcommands.Add(BuildRun());
        sim.Subcommands.Add(list);
        sim.Subcommands.Add(show);
        sim.Subcommands.Add(result);
        sim.Subcommands.Add(input);
        sim.Subcommands.Add(delete);
        sim.Subcommands.Add(BuildCompare());
        return sim;
    }

    private static Command BuildRun()
    {
        var roadmapIdOption = new Option<Guid>("--roadmap-id") { Required = true };
        var assumptionSetIdOption = new Option<Guid>("--assumption-set-id") { Required = true };
        var horizonMonthsOption = new Option<int?>("--horizon-months") { Description = "Defaults to 60." };
        var pathsOption = new Option<int?>("--paths") { Description = "Monte Carlo paths. Defaults to 1000." };
        var seedOption = new Option<int?>("--seed") { Description = "Defaults to 0." };
        var discountRateOption = new Option<double?>("--discount-rate") { Description = "Annual discount rate. Defaults to 0.10." };
        var terminalGrowthOption = new Option<double?>("--terminal-growth") { Description = "Annual terminal growth. Defaults to 0.02." };
        var nameOption = new Option<string?>("--name");
        var startDateOption = new Option<string?>("--start-date") { Description = "yyyy-MM-dd. Defaults to the first of the current month." };
        var marketIdOption = new Option<Guid?>("--market-id") { Description = "Restricts to one market; omit to simulate the whole workspace." };
        var eventIdsOption = new Option<string?>("--event-ids") { Description = "Comma-separated real MarketEvent ids to overlay." };
        var hypotheticalEventIdsOption = new Option<string?>("--hypothetical-event-ids") { Description = "Comma-separated hypothetical MarketEvent ids to inject." };

        var cmd = new Command("run", "Executes one simulation synchronously and prints the run id plus a headline-metrics digest.");
        cmd.Options.Add(roadmapIdOption);
        cmd.Options.Add(assumptionSetIdOption);
        cmd.Options.Add(horizonMonthsOption);
        cmd.Options.Add(pathsOption);
        cmd.Options.Add(seedOption);
        cmd.Options.Add(discountRateOption);
        cmd.Options.Add(terminalGrowthOption);
        cmd.Options.Add(nameOption);
        cmd.Options.Add(startDateOption);
        cmd.Options.Add(marketIdOption);
        cmd.Options.Add(eventIdsOption);
        cmd.Options.Add(hypotheticalEventIdsOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateSimulationRunRequest(
                parseResult.GetValue(roadmapIdOption), parseResult.GetValue(assumptionSetIdOption),
                parseResult.GetValue(horizonMonthsOption) ?? 60,
                parseResult.GetValue(pathsOption) ?? 1000,
                parseResult.GetValue(seedOption) ?? 0,
                parseResult.GetValue(discountRateOption) ?? 0.10,
                parseResult.GetValue(terminalGrowthOption) ?? 0.02,
                parseResult.GetValue(nameOption),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(startDateOption), "--start-date"),
                parseResult.GetValue(marketIdOption),
                ParseHelpers.ParseGuidList(parseResult.GetValue(eventIdsOption)),
                ParseHelpers.ParseGuidList(parseResult.GetValue(hypotheticalEventIdsOption)));

            var run = await client.CreateSimulationRunAsync(request, ct2);
            var engineResult = await client.GetSimulationRunResultAsync(run.Id, ct2);
            var headline = SimulationSummary.Summarize(engineResult);

            CliInfrastructure.WriteJson(new
            {
                runId = run.Id,
                name = run.Name,
                horizonMonths = run.HorizonMonths,
                paths = run.Paths,
                npvMean = headline.NpvMean,
                npvP10 = headline.NpvP10,
                npvP50 = headline.NpvP50,
                npvP90 = headline.NpvP90,
                terminalArr = headline.TerminalArr,
                cumulativeBookings = headline.CumulativeBookings
            });
        }, ct));
        return cmd;
    }

    private static Command BuildCompare()
    {
        var runAArg = new Argument<Guid>("run-a");
        var runBArg = new Argument<Guid>("run-b");

        var cmd = new Command("compare", "Fetches two runs' results and prints a side-by-side delta table of headline metrics (B minus A).");
        cmd.Arguments.Add(runAArg);
        cmd.Arguments.Add(runBArg);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var runA = parseResult.GetValue(runAArg);
            var runB = parseResult.GetValue(runBArg);

            var resultA = await client.GetSimulationRunResultAsync(runA, ct2);
            var resultB = await client.GetSimulationRunResultAsync(runB, ct2);
            var rows = SimulationSummary.Compare(resultA, resultB);

            CliInfrastructure.WriteJson(new
            {
                runA,
                runB,
                metrics = rows.Select(r => new
                {
                    metric = r.Metric,
                    a = r.A,
                    b = r.B,
                    delta = r.Delta,
                    percentDelta = r.PercentDelta
                })
            });
        }, ct));
        return cmd;
    }
}
