using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>Immutable fits: the prior assumption set plus the facts ledger as of a date produce a
/// new, fitted assumption set, diffable against the prior.</summary>
internal static class CalibrationCommands
{
    public static Command Build()
    {
        var calibrate = new Command("calibrate", "Fits an assumption set's engine parameters to the facts ledger as of a date, and inspects the resulting diffs.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var priorAssumptionSetIdOption = new Option<Guid?>("--prior-assumption-set-id");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists calibration runs.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(priorAssumptionSetIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListCalibrationRunsAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(priorAssumptionSetIdOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var show = new Command("show", "Shows a calibration run's metadata.");
        show.Arguments.Add(idArg);
        show.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetCalibrationRunAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var diff = new Command("diff", "Prints the run's prior-vs-posterior diff table (parameter, prior mean±sd, posterior mean±sd, shift, N).");
        diff.Arguments.Add(idArg);
        diff.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var result = await client.GetCalibrationRunDiffAsync(parseResult.GetValue(idArg), ct2);
            PrintDiff(result);
        }, ct));

        var delete = new Command("delete", "Deletes a calibration run (a stored fit is never edited, only deleted).");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteCalibrationRunAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        calibrate.Subcommands.Add(BuildRun());
        calibrate.Subcommands.Add(list);
        calibrate.Subcommands.Add(show);
        calibrate.Subcommands.Add(diff);
        calibrate.Subcommands.Add(delete);
        return calibrate;
    }

    private static Command BuildRun()
    {
        var assumptionSetIdOption = new Option<Guid>("--assumption-set") { Required = true, Description = "The prior AssumptionSet to fit." };
        var asOfOption = new Option<string?>("--as-of") { Description = "yyyy-MM-dd. Only facts recorded on or before this date are read. Defaults to today." };
        var nameOption = new Option<string?>("--name");
        var notesOption = new Option<string?>("--notes");

        var cmd = new Command("run", "Runs a fit synchronously and prints the run id plus the prior-vs-posterior diff table.");
        cmd.Options.Add(assumptionSetIdOption);
        cmd.Options.Add(asOfOption);
        cmd.Options.Add(nameOption);
        cmd.Options.Add(notesOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateCalibrationRunRequest(
                parseResult.GetValue(assumptionSetIdOption),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(asOfOption), "--as-of"),
                parseResult.GetValue(nameOption), parseResult.GetValue(notesOption));

            var result = await client.CreateCalibrationRunAsync(request, ct2);
            Console.WriteLine($"Calibration run {result.Run.Id} (as of {result.Run.FactsAsOfDate:yyyy-MM-dd}), " +
                               $"fitted assumption set {result.Run.FittedAssumptionSetId}:");
            PrintDiff(result.Diff);
        }, ct));
        return cmd;
    }

    private static void PrintDiff(CalibrationDiffDto diff)
    {
        var rows = CalibrationSummary.BuildRows(diff);
        Console.WriteLine(CalibrationSummary.FormatTable(rows));
        var unfitted = CalibrationSummary.FormatUnfitted(diff);
        if (unfitted.Length > 0)
            Console.WriteLine(unfitted);
    }
}
