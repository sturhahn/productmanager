using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>Versioned, branchable parameter collections that feed the simulation engine.</summary>
internal static class AssumptionSetCommands
{
    public static Command Build()
    {
        var assumptions = new Command("assumptions", "Versioned, branchable parameter collections — every parameter carries a distribution, a provenance, and (optionally) a rationale.");

        var idArg = new Argument<Guid>("id");
        var workspaceIdOption = new Option<Guid?>("--workspace-id");
        var parentIdOption = new Option<Guid?>("--parent-id");
        var nameOption = new Option<string>("--name") { Required = true };
        var notesOption = new Option<string?>("--notes");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists assumption sets.");
        list.Options.Add(workspaceIdOption);
        list.Options.Add(parentIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListAssumptionSetsAsync(
                parseResult.GetValue(workspaceIdOption), parseResult.GetValue(parentIdOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets an assumption set by id, including its parameters.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetAssumptionSetAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates an (empty) assumption set.");
        create.Options.Add(workspaceIdOption);
        create.Options.Add(nameOption);
        create.Options.Add(notesOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var workspaceId = parseResult.GetValue(workspaceIdOption) ?? throw new ArgumentException("--workspace-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateAssumptionSetRequest(workspaceId, parseResult.GetValue(nameOption)!, parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.CreateAssumptionSetAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Renames an assumption set / changes its notes.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(notesOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateAssumptionSetRequest(parseResult.GetValue(nameOption)!, parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.UpdateAssumptionSetAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes an assumption set (refused while runs or branches still reference it).");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteAssumptionSetAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        var branch = new Command("branch", "Branches the set: a new set with a deep copy of every parameter and a pointer back to this one.");
        branch.Arguments.Add(idArg);
        branch.Options.Add(nameOption);
        branch.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.BranchAssumptionSetAsync(
                parseResult.GetValue(idArg), new BranchAssumptionSetRequest(parseResult.GetValue(nameOption)!), ct2));
        }, ct));

        assumptions.Subcommands.Add(list);
        assumptions.Subcommands.Add(get);
        assumptions.Subcommands.Add(create);
        assumptions.Subcommands.Add(update);
        assumptions.Subcommands.Add(delete);
        assumptions.Subcommands.Add(branch);
        assumptions.Subcommands.Add(BuildSetParam(idArg));
        assumptions.Subcommands.Add(BuildDeleteParam(idArg));
        return assumptions;
    }

    private static Command BuildSetParam(Argument<Guid> idArg)
    {
        var distTypeOption = new Option<string>("--dist-type") { Required = true, Description = "Point | Normal | Lognormal | Beta" };
        var p1Option = new Option<double>("--p1") { Required = true, Description = "Point: the value. Normal: mean. Lognormal: mu. Beta: alpha." };
        var p2Option = new Option<double>("--p2") { Description = "Point: must be 0. Normal: std dev. Lognormal: sigma. Beta: beta." };
        var provenanceOption = new Option<string?>("--provenance") { Description = "Asserted | Calibrated | Default. Defaults to Asserted." };
        var rationaleOption = new Option<string?>("--rationale");
        var segmentIdOption = new Option<Guid?>("--segment-id") { Description = "+ --flow-kind: writes an engine parameter." };
        var flowKindOption = new Option<string?>("--flow-kind") { Description = "NewFirmArrival | ConversionToMarket | WinRate | ChurnHazard | CrossSellRate" };
        var moduleIdOption = new Option<Guid?>("--module-id") { Description = "Required with --flow-kind CrossSellRate." };
        var covariateIdOption = new Option<Guid?>("--covariate-id");
        var covariateBetaOption = new Option<double?>("--covariate-beta")
        {
            Description = "Elasticity to the covariate: rate * (x_t / x_0)^beta, i.e. +1% covariate => +beta% rate.",
        };
        var keyOption = new Option<string?>("--key") { Description = "Free-form key, for a parameter the engine ignores. Alternative to --segment-id + --flow-kind." };

        var cmd = new Command("set-param", "Sets (upserting) one parameter. Give --segment-id + --flow-kind (+ --module-id for CrossSellRate) for a parameter the engine reads, or --key for a free-form one.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(distTypeOption);
        cmd.Options.Add(p1Option);
        cmd.Options.Add(p2Option);
        cmd.Options.Add(provenanceOption);
        cmd.Options.Add(rationaleOption);
        cmd.Options.Add(segmentIdOption);
        cmd.Options.Add(flowKindOption);
        cmd.Options.Add(moduleIdOption);
        cmd.Options.Add(covariateIdOption);
        cmd.Options.Add(covariateBetaOption);
        cmd.Options.Add(keyOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var segmentId = parseResult.GetValue(segmentIdOption);
            var flowKind = ParseHelpers.ParseOptionalEnum<SimulationFlowKind>(parseResult.GetValue(flowKindOption), "--flow-kind");
            var key = parseResult.GetValue(keyOption);

            // Fails fast, client-side, with the same rule AssumptionSetsController.UpsertParameter enforces.
            ParameterKeyHelper.Validate(segmentId, flowKind, key);

            var distribution = new DistributionSpecDto(
                ParseHelpers.ParseEnum<DistributionType>(parseResult.GetValue(distTypeOption)!, "--dist-type"),
                parseResult.GetValue(p1Option), parseResult.GetValue(p2Option));

            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpsertParameterRequest(
                distribution,
                ParseHelpers.ParseOptionalEnum<ParameterProvenance>(parseResult.GetValue(provenanceOption), "--provenance") ?? ParameterProvenance.Asserted,
                parseResult.GetValue(rationaleOption),
                segmentId, flowKind, parseResult.GetValue(moduleIdOption),
                parseResult.GetValue(covariateIdOption), parseResult.GetValue(covariateBetaOption),
                key);
            CliInfrastructure.WriteJson(await client.UpsertParameterAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildDeleteParam(Argument<Guid> idArg)
    {
        var parameterIdOption = new Option<Guid>("--parameter-id") { Required = true };
        var cmd = new Command("delete-param", "Removes one parameter from an assumption set by its id (see 'assumptions get' for parameter ids).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(parameterIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.RemoveParameterAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(parameterIdOption), ct2));
        }, ct));
        return cmd;
    }
}
