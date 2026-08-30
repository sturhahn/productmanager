using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>Routes to market: reach into segments, opportunity origination, per-product bias.</summary>
internal static class ChannelCommands
{
    public static Command Build()
    {
        var channel = new Command("channel", "Routes to market — reach into segments, opportunity origination, and per-product bias. Direct sales is just another channel.");

        var idArg = new Argument<Guid>("id");
        var marketIdOption = new Option<Guid?>("--market-id");
        var originatesOption = new Option<bool?>("--originates-opportunities");
        var nameOption = new Option<string>("--name") { Required = true };
        var descriptionOption = new Option<string?>("--description");
        var carriesOnlyListedOption = new Option<bool>("--carries-only-listed") { Description = "Turns the affinity list into a whitelist: products not listed drop out of consideration." };
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists channels.");
        list.Options.Add(marketIdOption);
        list.Options.Add(originatesOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListChannelsAsync(
                parseResult.GetValue(marketIdOption), parseResult.GetValue(originatesOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a channel by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetChannelAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a channel.");
        create.Options.Add(marketIdOption);
        create.Options.Add(nameOption);
        create.Options.Add(descriptionOption);
        create.Options.Add(originatesOption);
        create.Options.Add(carriesOnlyListedOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var marketId = parseResult.GetValue(marketIdOption) ?? throw new ArgumentException("--market-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateChannelRequest(
                marketId, parseResult.GetValue(nameOption)!, parseResult.GetValue(descriptionOption),
                parseResult.GetValue(originatesOption) ?? false, parseResult.GetValue(carriesOnlyListedOption));
            CliInfrastructure.WriteJson(await client.CreateChannelAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates a channel.");
        update.Arguments.Add(idArg);
        update.Options.Add(nameOption);
        update.Options.Add(descriptionOption);
        update.Options.Add(originatesOption);
        update.Options.Add(carriesOnlyListedOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateChannelRequest(
                parseResult.GetValue(nameOption)!, parseResult.GetValue(descriptionOption),
                parseResult.GetValue(originatesOption) ?? false, parseResult.GetValue(carriesOnlyListedOption));
            CliInfrastructure.WriteJson(await client.UpdateChannelAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a channel.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteChannelAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        channel.Subcommands.Add(list);
        channel.Subcommands.Add(get);
        channel.Subcommands.Add(create);
        channel.Subcommands.Add(update);
        channel.Subcommands.Add(delete);
        channel.Subcommands.Add(BuildSetSegmentReach(idArg));
        channel.Subcommands.Add(BuildRemoveSegmentReach(idArg));
        channel.Subcommands.Add(BuildSetProductAffinity(idArg));
        channel.Subcommands.Add(BuildRemoveProductAffinity(idArg));
        return channel;
    }

    private static Command BuildSetSegmentReach(Argument<Guid> idArg)
    {
        var segmentIdOption = new Option<Guid>("--segment-id") { Required = true };
        var reachOption = new Option<double>("--reach") { Required = true, Description = "Fraction [0,1] of the segment this channel can reach." };
        var originationShareOption = new Option<double>("--origination-share") { Description = "Fraction [0,1] of the segment's opportunity flow this channel originates. Defaults to 0." };

        var cmd = new Command("set-segment-reach", "Sets (upserting by segment) how much of a segment this channel reaches and originates.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(segmentIdOption);
        cmd.Options.Add(reachOption);
        cmd.Options.Add(originationShareOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new SetChannelSegmentReachRequest(
                parseResult.GetValue(segmentIdOption), parseResult.GetValue(reachOption), parseResult.GetValue(originationShareOption));
            CliInfrastructure.WriteJson(await client.SetChannelSegmentReachAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildRemoveSegmentReach(Argument<Guid> idArg)
    {
        var segmentIdOption = new Option<Guid>("--segment-id") { Required = true };
        var cmd = new Command("remove-segment-reach", "Removes a channel's reach entry for one segment.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(segmentIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.RemoveChannelSegmentReachAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(segmentIdOption), ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildSetProductAffinity(Argument<Guid> idArg)
    {
        var productIdOption = new Option<Guid>("--product-id") { Required = true };
        var biasOption = new Option<double>("--bias") { Required = true, Description = "Signed utility bias toward this product. 0 is neutral." };

        var cmd = new Command("set-product-affinity", "Sets (upserting by product) this channel's signed bias toward a product.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(productIdOption);
        cmd.Options.Add(biasOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new SetChannelProductAffinityRequest(parseResult.GetValue(productIdOption), parseResult.GetValue(biasOption));
            CliInfrastructure.WriteJson(await client.SetChannelProductAffinityAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));
        return cmd;
    }

    private static Command BuildRemoveProductAffinity(Argument<Guid> idArg)
    {
        var productIdOption = new Option<Guid>("--product-id") { Required = true };
        var cmd = new Command("remove-product-affinity", "Removes a channel's affinity entry for one product.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(productIdOption);
        cmd.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.RemoveChannelProductAffinityAsync(
                parseResult.GetValue(idArg), parseResult.GetValue(productIdOption), ct2));
        }, ct));
        return cmd;
    }
}

/// <summary>Product↔Product edges with a quality score, including integrations with partial competitors.</summary>
internal static class IntegrationCommands
{
    public static Command Build()
    {
        var integration = new Command("integration", "Product-to-product integration edges, including integrations with partial competitors.");

        var idArg = new Argument<Guid>("id");
        var productIdOption = new Option<Guid?>("--product-id") { Description = "Filters to integrations touching this product." };
        var productAOption = new Option<Guid>("--product-a") { Required = true };
        var productBOption = new Option<Guid>("--product-b") { Required = true };
        var qualityOption = new Option<double>("--quality") { Required = true, Description = "Quality score in [0,1]." };
        var notesOption = new Option<string?>("--notes");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists integrations.");
        list.Options.Add(productIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListIntegrationsAsync(
                parseResult.GetValue(productIdOption), parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets an integration by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetIntegrationAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates an integration edge. The edge is symmetric — posting the reverse pair is a conflict.");
        create.Options.Add(productAOption);
        create.Options.Add(productBOption);
        create.Options.Add(qualityOption);
        create.Options.Add(notesOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateIntegrationRequest(
                parseResult.GetValue(productAOption), parseResult.GetValue(productBOption),
                parseResult.GetValue(qualityOption), parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.CreateIntegrationAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Updates an integration's quality score / notes.");
        update.Arguments.Add(idArg);
        update.Options.Add(qualityOption);
        update.Options.Add(notesOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateIntegrationRequest(parseResult.GetValue(qualityOption), parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.UpdateIntegrationAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes an integration.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteIntegrationAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        integration.Subcommands.Add(list);
        integration.Subcommands.Add(get);
        integration.Subcommands.Add(create);
        integration.Subcommands.Add(update);
        integration.Subcommands.Add(delete);
        return integration;
    }
}

/// <summary>"Being a client" as an edge — firm holds product, serving these processes, at this share
/// of list price. The same shape carries competitors' client relationships.</summary>
internal static class FirmRelationshipCommands
{
    public static Command Build()
    {
        var relationship = new Command("firm-relationship", "Firm-to-product client relationships: modules held, price level, acquisition/end dates.");

        var idArg = new Argument<Guid>("id");
        var firmIdOption = new Option<Guid?>("--firm-id");
        var productIdOption = new Option<Guid?>("--product-id");
        var activeOnlyOption = new Option<bool?>("--active-only");
        var servedProcessIdsOption = new Option<string?>("--served-process-ids") { Description = "Comma-separated business process ids. Empty means \"whatever the product covers\"." };
        var priceLevelOption = new Option<double?>("--price-level") { Description = "Multiplier on list price (1.0 = list). Defaults to 1.0." };
        var acquisitionDateOption = new Option<string?>("--acquisition-date") { Description = "yyyy-MM-dd" };
        var endDateOption = new Option<string?>("--end-date") { Description = "yyyy-MM-dd. Omitting on update reopens the relationship." };
        var notesOption = new Option<string?>("--notes");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists firm-product relationships.");
        list.Options.Add(firmIdOption);
        list.Options.Add(productIdOption);
        list.Options.Add(activeOnlyOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListFirmRelationshipsAsync(
                parseResult.GetValue(firmIdOption), parseResult.GetValue(productIdOption), parseResult.GetValue(activeOnlyOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a firm-product relationship by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetFirmRelationshipAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var create = new Command("create", "Creates a firm-product relationship.");
        create.Options.Add(firmIdOption);
        create.Options.Add(productIdOption);
        create.Options.Add(servedProcessIdsOption);
        create.Options.Add(priceLevelOption);
        create.Options.Add(acquisitionDateOption);
        create.Options.Add(notesOption);
        create.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var firmId = parseResult.GetValue(firmIdOption) ?? throw new ArgumentException("--firm-id is required.");
            var productId = parseResult.GetValue(productIdOption) ?? throw new ArgumentException("--product-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateFirmProductRelationshipRequest(
                firmId, productId, ParseHelpers.ParseGuidList(parseResult.GetValue(servedProcessIdsOption)),
                parseResult.GetValue(priceLevelOption) ?? 1.0, PricePaidPerPeriod: null,
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(acquisitionDateOption), "--acquisition-date"),
                parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.CreateFirmRelationshipAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Replaces a firm-product relationship. Omitting --end-date reopens it.");
        update.Arguments.Add(idArg);
        update.Options.Add(servedProcessIdsOption);
        update.Options.Add(priceLevelOption);
        update.Options.Add(acquisitionDateOption);
        update.Options.Add(endDateOption);
        update.Options.Add(notesOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateFirmProductRelationshipRequest(
                ParseHelpers.ParseGuidList(parseResult.GetValue(servedProcessIdsOption)),
                parseResult.GetValue(priceLevelOption) ?? 1.0, PricePaidPerPeriod: null,
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(acquisitionDateOption), "--acquisition-date"),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(endDateOption), "--end-date"),
                parseResult.GetValue(notesOption));
            CliInfrastructure.WriteJson(await client.UpdateFirmRelationshipAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a firm-product relationship.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteFirmRelationshipAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        relationship.Subcommands.Add(list);
        relationship.Subcommands.Add(get);
        relationship.Subcommands.Add(create);
        relationship.Subcommands.Add(update);
        relationship.Subcommands.Add(delete);
        return relationship;
    }
}
