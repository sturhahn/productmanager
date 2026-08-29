using System.CommandLine;
using System.Globalization;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>Named external time series that drive simulated flows — an equity index, a rate, a
/// volume. Observed history lives in the facts ledger; the future is a GBM scenario spec.</summary>
internal static class CovariateCommands
{
    public static Command Build()
    {
        var covariate = new Command("covariate", "Named external time series (e.g. an equity index) that can drive simulated flows via a parameter's covariate link.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var nameOption = new Option<string>("--name") { Required = true };
        var descriptionOption = new Option<string?>("--description");
        var unitOption = new Option<string?>("--unit");
        var gbmStartOption = new Option<double?>("--gbm-start") { Description = "Future-scenario GBM: month-0 level. Give all three --gbm-* together, or none." };
        var gbmDriftOption = new Option<double?>("--gbm-drift") { Description = "Future-scenario GBM: per-month log drift." };
        var gbmVolOption = new Option<double?>("--gbm-vol") { Description = "Future-scenario GBM: per-month log volatility." };
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists covariates.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListCovariatesAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a covariate by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetCovariateAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a covariate.");
        create.Options.Add(workspaceIdOption);
        create.Options.Add(nameOption);
        create.Options.Add(descriptionOption);
        create.Options.Add(unitOption);
        create.Options.Add(gbmStartOption);
        create.Options.Add(gbmDriftOption);
        create.Options.Add(gbmVolOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var workspaceId = parseResult.GetValue(workspaceIdOption) ?? throw new ArgumentException("--workspace-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateCovariateRequest(
                workspaceId, parseResult.GetValue(nameOption)!, parseResult.GetValue(descriptionOption), parseResult.GetValue(unitOption),
                parseResult.GetValue(gbmStartOption), parseResult.GetValue(gbmDriftOption), parseResult.GetValue(gbmVolOption));
            CliInfrastructure.WriteJson(await client.CreateCovariateAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates a covariate.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(descriptionOption);
        update.Options.Add(unitOption);
        update.Options.Add(gbmStartOption);
        update.Options.Add(gbmDriftOption);
        update.Options.Add(gbmVolOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateCovariateRequest(
                parseResult.GetValue(nameOption)!, parseResult.GetValue(descriptionOption), parseResult.GetValue(unitOption),
                parseResult.GetValue(gbmStartOption), parseResult.GetValue(gbmDriftOption), parseResult.GetValue(gbmVolOption));
            CliInfrastructure.WriteJson(await client.UpdateCovariateAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a covariate.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteCovariateAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        covariate.Subcommands.Add(list);
        covariate.Subcommands.Add(get);
        covariate.Subcommands.Add(create);
        covariate.Subcommands.Add(update);
        covariate.Subcommands.Add(delete);
        covariate.Subcommands.Add(BuildAddObservation(idArg));
        covariate.Subcommands.Add(BuildHistory(idArg));
        return covariate;
    }

    private static Command BuildAddObservation(Argument<Guid> idArg)
    {
        var workspaceIdOption = new Option<Guid>("--workspace-id") { Required = true };
        var effectiveDateOption = new Option<string>("--effective-date") { Required = true, Description = "yyyy-MM-dd — when the value was true in the world." };
        var valueOption = new Option<double>("--value") { Required = true };
        var notesOption = new Option<string?>("--notes");

        var cmd = new Command("add-observation", "Appends one observed reading for this covariate (a CovariateObserved fact).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(workspaceIdOption);
        cmd.Options.Add(effectiveDateOption);
        cmd.Options.Add(valueOption);
        cmd.Options.Add(notesOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var payload = $"{{\"value\":{parseResult.GetValue(valueOption).ToString(CultureInfo.InvariantCulture)}}}";
            var request = new CreateFactEventRequest(
                parseResult.GetValue(workspaceIdOption),
                FactEventKind.CovariateObserved,
                ParseHelpers.ParseDate(parseResult.GetValue(effectiveDateOption)!, "--effective-date"),
                SubjectType: "Covariate",
                SubjectId: parseResult.GetValue(idArg),
                PayloadJson: payload,
                Notes: parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.CreateFactAsync(request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildHistory(Argument<Guid> idArg)
    {
        var effectiveFromOption = new Option<string?>("--effective-from") { Description = "yyyy-MM-dd" };
        var effectiveToOption = new Option<string?>("--effective-to") { Description = "yyyy-MM-dd" };
        var knownAsOfOption = new Option<string?>("--known-as-of") { Description = "ISO 8601 instant — reconstructs what was known at that time." };

        var cmd = new Command("history", "The covariate's observed history, read out of the facts ledger.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(effectiveFromOption);
        cmd.Options.Add(effectiveToOption);
        cmd.Options.Add(knownAsOfOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var knownAsOfText = parseResult.GetValue(knownAsOfOption);
            DateTimeOffset? knownAsOf = knownAsOfText is null
                ? null
                : DateTimeOffset.TryParse(knownAsOfText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : throw new ArgumentException($"'{knownAsOfText}' is not a valid --known-as-of instant.");

            CliInfrastructure.WriteJson(await client.GetCovariateHistoryAsync(
                parseResult.GetValue(idArg),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(effectiveFromOption), "--effective-from"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(effectiveToOption), "--effective-to"),
                knownAsOf, ct2));
        }, ct));
        return cmd;
    }
}
