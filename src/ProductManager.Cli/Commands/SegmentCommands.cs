using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>ClientSegment reads plus the Phase 2 stock-state / module-penetration writes — the
/// segment's own create/update/delete stay seed-file-only (see SeedLoader), as in Phase 1.</summary>
internal static class SegmentCommands
{
    public static Command Build()
    {
        var segment = new Command("segment", "Client segments — a market's demand-side buckets, plus the stocks and unit economics the simulation engine starts a run from.");

        var idArg = new Argument<Guid>("id");
        var marketIdOption = new Option<Guid?>("--market-id");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists client segments.");
        list.Options.Add(marketIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListClientSegmentsAsync(
                parseResult.GetValue(marketIdOption), parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a client segment by id, including its stock state and module penetrations.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetClientSegmentAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        segment.Subcommands.Add(list);
        segment.Subcommands.Add(get);
        segment.Subcommands.Add(BuildSetStocks(idArg));
        segment.Subcommands.Add(BuildSetModulePenetration(idArg));
        segment.Subcommands.Add(BuildRemoveModulePenetration(idArg));
        return segment;
    }

    private static Command BuildSetStocks(Argument<Guid> idArg)
    {
        var prospectPoolOption = new Option<double>("--prospect-pool") { Description = "Initial size of the addressable-but-not-yet-client pool." };
        var clientCountOption = new Option<int>("--client-count") { Description = "Initial number of clients already won." };
        var arpuOption = new Option<double>("--arpu") { Description = "Recurring revenue per client per month." };
        var implementationFeeOption = new Option<double>("--implementation-fee");
        var servicingCostOption = new Option<double>("--servicing-cost") { Description = "Servicing cost per client per month." };
        var infraFixedOption = new Option<double>("--infra-fixed") { Description = "Fixed infrastructure cost per month, independent of client count." };
        var infraPerClientOption = new Option<double>("--infra-per-client");
        var rdDragOption = new Option<double>("--rd-drag") { Description = "R&D cost drag attributed to this segment per month." };

        var cmd = new Command("set-stocks", "Replaces a segment's whole starting state (stocks + unit economics) — one measurement, not a set of knobs.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(prospectPoolOption);
        cmd.Options.Add(clientCountOption);
        cmd.Options.Add(arpuOption);
        cmd.Options.Add(implementationFeeOption);
        cmd.Options.Add(servicingCostOption);
        cmd.Options.Add(infraFixedOption);
        cmd.Options.Add(infraPerClientOption);
        cmd.Options.Add(rdDragOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new SetSegmentStockStateRequest(
                parseResult.GetValue(prospectPoolOption),
                parseResult.GetValue(clientCountOption),
                parseResult.GetValue(arpuOption),
                parseResult.GetValue(implementationFeeOption),
                parseResult.GetValue(servicingCostOption),
                parseResult.GetValue(infraFixedOption),
                parseResult.GetValue(infraPerClientOption),
                parseResult.GetValue(rdDragOption));
            CliInfrastructure.WriteJson(await client.SetSegmentStockStateAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildSetModulePenetration(Argument<Guid> idArg)
    {
        var moduleIdOption = new Option<Guid>("--module-id") { Required = true, Description = "The module Product id (upserts by module)." };
        var penetrationOption = new Option<double>("--penetration") { Required = true, Description = "Fraction [0,1] of the segment's clients already holding the module." };
        var arpuOption = new Option<double>("--arpu") { Required = true, Description = "Recurring revenue per client per month for this module." };

        var cmd = new Command("set-module-penetration", "Sets (upserting by module) one cross-sellable module's starting penetration and pricing in a segment.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(moduleIdOption);
        cmd.Options.Add(penetrationOption);
        cmd.Options.Add(arpuOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new SetSegmentModulePenetrationRequest(
                parseResult.GetValue(moduleIdOption), parseResult.GetValue(penetrationOption), parseResult.GetValue(arpuOption));
            CliInfrastructure.WriteJson(await client.SetSegmentModulePenetrationAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildRemoveModulePenetration(Argument<Guid> idArg)
    {
        var moduleIdOption = new Option<Guid>("--module-id") { Required = true };
        var cmd = new Command("remove-module-penetration", "Removes a module's penetration/pricing entry from a segment.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(moduleIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.RemoveSegmentModulePenetrationAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(moduleIdOption), ct2));
        }, ct));
        return cmd;
    }
}
