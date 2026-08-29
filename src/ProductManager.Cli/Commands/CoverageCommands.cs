using System.CommandLine;
using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>Coverage assessments — how well a product covers a business process, per approach/segment.</summary>
internal static class CoverageCommands
{
    public static Command Build()
    {
        var coverage = new Command("coverage", "Coverage assessments — how well a product covers a business process, optionally per approach/segment.");

        var idArg = new Argument<Guid>("id");
        var productIdOption = new Option<Guid?>("--product-id");
        var businessProcessIdOption = new Option<Guid?>("--business-process-id");
        var segmentIdOption = new Option<Guid?>("--segment-id");
        var approachIdOption = new Option<Guid?>("--approach-id");
        var tierOption = new Option<string>("--tier") { Required = true, Description = "NotPlaying | Bronze | Silver | Gold" };
        var isUnknownOption = new Option<bool>("--is-unknown") { Description = "Records an explicit \"we don't know\" and ignores --tier/--confidence." };
        var confidenceOption = new Option<double?>("--confidence") { Description = "[0,1]. Defaults to 1.0." };
        var effectiveDateOption = new Option<string?>("--effective-date") { Description = "yyyy-MM-dd" };
        var rationaleOption = new Option<string?>("--rationale");
        var scaleLimitsOption = new Option<string?>("--scale-limits");
        var pageOption = new Option<int?>("--page");
        var pageSizeOption = new Option<int?>("--page-size");

        var list = new Command("list", "Lists coverage assessments.");
        list.Options.Add(productIdOption);
        list.Options.Add(businessProcessIdOption);
        list.Options.Add(segmentIdOption);
        list.Options.Add(pageOption);
        list.Options.Add(pageSizeOption);
        list.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.ListCoveragesAsync(
                parseResult.GetValue(productIdOption), parseResult.GetValue(businessProcessIdOption), parseResult.GetValue(segmentIdOption),
                parseResult.GetValue(pageOption), parseResult.GetValue(pageSizeOption), ct2));
        }, ct));

        var get = new Command("get", "Gets a coverage assessment by id.");
        get.Arguments.Add(idArg);
        get.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            CliInfrastructure.WriteJson(await client.GetCoverageAsync(parseResult.GetValue(idArg), ct2));
        }, ct));

        var upsert = new Command("upsert", "Records a coverage assessment, upserting on the (product, process, approach?, segment?) key — posting the same tuple twice re-assesses rather than duplicating.");
        upsert.Options.Add(productIdOption);
        upsert.Options.Add(businessProcessIdOption);
        upsert.Options.Add(tierOption);
        upsert.Options.Add(isUnknownOption);
        upsert.Options.Add(confidenceOption);
        upsert.Options.Add(approachIdOption);
        upsert.Options.Add(segmentIdOption);
        upsert.Options.Add(effectiveDateOption);
        upsert.Options.Add(rationaleOption);
        upsert.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var productId = parseResult.GetValue(productIdOption) ?? throw new ArgumentException("--product-id is required.");
            var businessProcessId = parseResult.GetValue(businessProcessIdOption) ?? throw new ArgumentException("--business-process-id is required.");
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new CreateCoverageRequest(
                productId, businessProcessId,
                ParseHelpers.ParseEnum<CoverageTier>(parseResult.GetValue(tierOption)!, "--tier"),
                parseResult.GetValue(isUnknownOption),
                parseResult.GetValue(confidenceOption) ?? 1.0,
                parseResult.GetValue(approachIdOption), parseResult.GetValue(segmentIdOption),
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(effectiveDateOption), "--effective-date"),
                parseResult.GetValue(rationaleOption));
            CliInfrastructure.WriteJson(await client.CreateCoverageAsync(request, ct2));
        }, ct));

        var update = new Command("update", "Re-assesses an existing coverage row by id; the key dimensions (product/process/approach/segment) are immutable.");
        update.Arguments.Add(idArg);
        update.Options.Add(tierOption);
        update.Options.Add(isUnknownOption);
        update.Options.Add(confidenceOption);
        update.Options.Add(effectiveDateOption);
        update.Options.Add(rationaleOption);
        update.Options.Add(scaleLimitsOption);
        update.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var request = new UpdateCoverageRequest(
                ParseHelpers.ParseEnum<CoverageTier>(parseResult.GetValue(tierOption)!, "--tier"),
                parseResult.GetValue(isUnknownOption),
                parseResult.GetValue(confidenceOption) ?? 1.0,
                ParseHelpers.ParseOptionalDate(parseResult.GetValue(effectiveDateOption), "--effective-date"),
                parseResult.GetValue(rationaleOption), parseResult.GetValue(scaleLimitsOption));
            CliInfrastructure.WriteJson(await client.UpdateCoverageAsync(parseResult.GetValue(idArg), request, ct2));
        }, ct));

        var delete = new Command("delete", "Deletes a coverage assessment.");
        delete.Arguments.Add(idArg);
        delete.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var client = CliInfrastructure.BuildAuthenticatedClient(parseResult);
            var id = parseResult.GetValue(idArg);
            await client.DeleteCoverageAsync(id, ct2);
            CliInfrastructure.WriteJson(new { deleted = id });
        }, ct));

        coverage.Subcommands.Add(list);
        coverage.Subcommands.Add(get);
        coverage.Subcommands.Add(upsert);
        coverage.Subcommands.Add(update);
        coverage.Subcommands.Add(delete);
        return coverage;
    }
}
