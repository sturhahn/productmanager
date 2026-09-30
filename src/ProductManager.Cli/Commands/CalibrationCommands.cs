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
            var id = parseResult.GetValue(idArg);
            // GetCalibrationRunDiffAsync's DTO doesn't carry the workspace id, so fetch the run's
            // metadata too — just to resolve the diff table's parameter keys to names.
            var run = await client.GetCalibrationRunAsync(id, ct2);
            var result = await client.GetCalibrationRunDiffAsync(id, ct2);
            var names = await NameResolver.BuildAsync(client, run.WorkspaceId, ct2);
            PrintDiff(result, names);
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
        calibrate.Subcommands.Add(BuildReadiness());
        calibrate.Subcommands.Add(delete);
        return calibrate;
    }

    private static Command BuildRun()
    {
        var assumptionSetIdOption = new Option<Guid>("--assumption-set") { Required = true, Description = "The prior AssumptionSet to fit." };
        var asOfOption = new Option<string?>("--as-of") { Description = "yyyy-MM-dd. Only facts recorded on or before this date are read. Defaults to today." };
        var nameOption = new Option<string?>("--name");
        var notesOption = new Option<string?>("--notes");
        var afterOption = new Option<string?>("--observations-recorded-after")
        {
            Description = "yyyy-MM-dd. Fit only facts recorded after this date (relationship exposure starts here too) — " +
                          "a sequential update of a prior fitted through it. Must be before --as-of."
        };

        var cmd = new Command("run", "Runs a fit synchronously and prints the run id plus the prior-vs-posterior diff table.");
        cmd.Options.Add(assumptionSetIdOption);
        cmd.Options.Add(asOfOption);
        cmd.Options.Add(nameOption);
        cmd.Options.Add(notesOption);
        cmd.Options.Add(afterOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateCalibrationRunRequest(
                parseResult.GetValue(assumptionSetIdOption),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(asOfOption), "--as-of"),
                parseResult.GetValue(nameOption), parseResult.GetValue(notesOption),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(afterOption), "--observations-recorded-after"));

            CalibrationRunResultDto result;
            try
            {
                result = await client.CreateCalibrationRunAsync(request, ct2);
            }
            catch (ApiRequestException ex) when (ex.IsPriorEvidenceReuse)
            {
                throw new ExitCodeException(6, CalibrationSummary.EvidenceReuseMessage(ex.Detail));
            }

            Console.WriteLine($"Calibration run {result.Run.Id} (as of {result.Run.FactsAsOfDate:yyyy-MM-dd}), " +
                               $"fitted assumption set {result.Run.FittedAssumptionSetId}:");
            var names = await NameResolver.BuildAsync(client, result.Run.WorkspaceId, ct2);
            PrintDiff(result.Diff, names);
        }, ct));
        return cmd;
    }

    private static Command BuildReadiness()
    {
        var idArg = new Argument<Guid>("id");
        var holdoutOption = new Option<string?>("--holdout-through")
        {
            Description = "yyyy-MM-dd, after the run's as-of date. Compares fitted rates with outcomes effective between " +
                          "the as-of date and this date. No held-out outcomes, or inputs needing history we do not have, " +
                          "report Unsupported — never a pass."
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Also write the full JSON report to this file (keep it with the run id in the import package)."
        };
        var jsonOption = new Option<bool>("--json") { Description = "Print the JSON report instead of the summary." };

        var cmd = new Command("readiness",
            "Prints the run's readiness report: per parameter and segment the evidence, denominator, prior evidence, " +
            "posterior and open issues; asserted choice-model quantities; optional holdout comparison. Computed on read; " +
            "the run is never refitted.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(holdoutOption);
        cmd.Options.Add(outputOption);
        cmd.Options.Add(jsonOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            var report = await client.GetCalibrationReadinessAsync(id,
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(holdoutOption), "--holdout-through"), ct2);

            if (parseResult.GetValue(outputOption) is { Length: > 0 } path)
            {
                await File.WriteAllTextAsync(path, CliInfrastructure.ToJson(report), ct2);
                await Console.Error.WriteLineAsync($"Wrote readiness report for run {id} to {path}.");
            }

            if (parseResult.GetValue(jsonOption))
            {
                CliInfrastructure.WriteJson(report);
                return;
            }

            var run = await client.GetCalibrationRunAsync(id, ct2);
            var names = await NameResolver.BuildAsync(client, run.WorkspaceId, ct2);
            Console.WriteLine(CalibrationSummary.FormatReadiness(report, names));
        }, ct));
        return cmd;
    }

    private static void PrintDiff(CalibrationDiffDto diff, NameResolver names)
    {
        var rows = CalibrationSummary.BuildRows(diff);
        Console.WriteLine(CalibrationSummary.FormatTable(rows, names));
        var unfitted = CalibrationSummary.FormatUnfitted(diff, names);
        if (unfitted.Length > 0)
            Console.WriteLine(unfitted);
    }
}
