using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>The central hinge of the market model: a firm seeking coverage for a scoped set of
/// business processes. Lifecycle is open -&gt; qualified -&gt; decided (won / lost / no-decision).</summary>
internal static class OpportunityCommands
{
    public static Command Build()
    {
        var opportunity = new Command("opportunity", "A firm seeking coverage for a scoped set of business processes — the central hinge of the market model.");

        var idArg = new Argument<Guid>("id");
        var firmIdOption = new Option<Guid?>("--firm-id");
        var channelIdOption = new Option<Guid?>("--channel-id");
        var stageOption = new Option<string?>("--stage") { Description = "Open | Qualified | Won | Lost | NoDecision" };
        var isCrossSellOption = new Option<bool?>("--is-cross-sell");
        var openedAtOption = new Option<string?>("--opened-at") { Description = "yyyy-MM-dd. Defaults to now." };
        var crossSellModuleOption = new Option<Guid?>("--cross-sell-module-product") { Description = "Marks this as a cross-sell opportunity for this module product." };
        var scopeProcessIdsOption = new Option<string?>("--scope-process-ids") { Description = "Comma-separated business process ids." };
        var competitorIdsOption = new Option<string?>("--competitor-ids") { Description = "Comma-separated competitor ids." };
        var notesOption = new Option<string?>("--notes");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists opportunities.");
        list.Options.Add(firmIdOption);
        list.Options.Add(channelIdOption);
        list.Options.Add(stageOption);
        list.Options.Add(isCrossSellOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListOpportunitiesAsync(
                parseResult.GetValue(firmIdOption), parseResult.GetValue(channelIdOption),
                ParseHelpers.ParseOptionalEnum<OpportunityStage>(parseResult.GetValue(stageOption), "--stage"),
                parseResult.GetValue(isCrossSellOption), parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets an opportunity by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetOpportunityAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Opens an opportunity.");
        create.Options.Add(firmIdOption);
        create.Options.Add(openedAtOption);
        create.Options.Add(channelIdOption);
        create.Options.Add(crossSellModuleOption);
        create.Options.Add(scopeProcessIdsOption);
        create.Options.Add(competitorIdsOption);
        create.Options.Add(notesOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var firmId = parseResult.GetValue(firmIdOption) ?? throw new ArgumentException("--firm-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateOpportunityRequest(
                firmId,
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(openedAtOption), "--opened-at") is { } opened
                    ? new DateTimeOffset(opened.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    : null,
                parseResult.GetValue(channelIdOption),
                parseResult.GetValue(crossSellModuleOption),
                ParseHelpers.ParseGuidList(parseResult.GetValue(scopeProcessIdsOption)),
                ParseHelpers.ParseGuidList(parseResult.GetValue(competitorIdsOption)),
                parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.CreateOpportunityAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates an opportunity's channel, cross-sell module, competitors, or notes.");
        update.Arguments.Add(idArg);
        update.Options.Add(channelIdOption);
        update.Options.Add(crossSellModuleOption);
        update.Options.Add(competitorIdsOption);
        update.Options.Add(notesOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateOpportunityRequest(
                parseResult.GetValue(channelIdOption), parseResult.GetValue(crossSellModuleOption),
                ParseHelpers.ParseGuidList(parseResult.GetValue(competitorIdsOption)), parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.UpdateOpportunityAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes an opportunity.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteOpportunityAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        opportunity.Subcommands.Add(list);
        opportunity.Subcommands.Add(get);
        opportunity.Subcommands.Add(create);
        opportunity.Subcommands.Add(update);
        opportunity.Subcommands.Add(delete);
        opportunity.Subcommands.Add(BuildAddScope(idArg));
        opportunity.Subcommands.Add(BuildRemoveScope(idArg));
        opportunity.Subcommands.Add(BuildQualify(idArg));
        opportunity.Subcommands.Add(BuildDecide(idArg));
        return opportunity;
    }

    private static Command BuildAddScope(Argument<Guid> idArg)
    {
        var processIdOption = new Option<Guid>("--business-process-id") { Required = true };
        var atOption = new Option<string?>("--at") { Description = "yyyy-MM-dd. Defaults to now." };

        var cmd = new Command("add-scope", "Adds one business process to the scope (idempotent). Refused once the deal is decided.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(processIdOption);
        cmd.Options.Add(atOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new AddOpportunityScopeRequest(parseResult.GetValue(processIdOption), ToUtcMidnightOrNull(parseResult.GetValue(atOption), "--at"));
            CliInfrastructure.WriteJson(await client.AddOpportunityScopeAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildRemoveScope(Argument<Guid> idArg)
    {
        var processIdOption = new Option<Guid>("--business-process-id") { Required = true };
        var cmd = new Command("remove-scope", "Removes one business process from the scope.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(processIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.RemoveOpportunityScopeAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(processIdOption), ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildQualify(Argument<Guid> idArg)
    {
        var atOption = new Option<string?>("--at") { Description = "yyyy-MM-dd. Defaults to now." };
        var cmd = new Command("qualify", "Qualifies the opportunity.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(atOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new QualifyOpportunityRequest(ToUtcMidnightOrNull(parseResult.GetValue(atOption), "--at"));
            CliInfrastructure.WriteJson(await client.QualifyOpportunityAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildDecide(Argument<Guid> idArg)
    {
        var outcomeOption = new Option<string>("--outcome") { Required = true, Description = "won | lost | no-decision" };
        var winnerProductOption = new Option<Guid?>("--winner-product") { Description = "Required when --outcome won: the winning product (ours or a competitor's)." };
        var atOption = new Option<string?>("--at") { Description = "yyyy-MM-dd. Defaults to now." };

        var cmd = new Command("decide", "Records the decision. A won deal must name the winning product. Also appends the OpportunityDecided fact calibration reads.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(outcomeOption);
        cmd.Options.Add(winnerProductOption);
        cmd.Options.Add(atOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var outcome = Phase3ParseHelpers.ParseOpportunityOutcome(parseResult.GetValue(outcomeOption)!);
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new DecideOpportunityRequest(
                outcome, parseResult.GetValue(winnerProductOption), ToUtcMidnightOrNull(parseResult.GetValue(atOption), "--at"));
            CliInfrastructure.WriteJson(await client.DecideOpportunityAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static DateTimeOffset? ToUtcMidnightOrNull(string? value, string optionName) =>
        ParseHelpers.ParseOptionalDate(value, optionName) is { } date
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
}
