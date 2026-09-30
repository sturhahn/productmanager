using ProductManager.Cli.Client;

namespace ProductManager.Cli.Seed;

/// <summary>
/// The checks on a seed file that need no API: source identities (complete, unique within the file),
/// <c>clearFields</c> usage, one row per firm/product edge, and firm names a reference could not tell
/// apart. Per-record parsing (dates, enums, lifecycle order) and reference resolution happen in the
/// loader's dry-run pass, which reports into the same problem list — see <see cref="SeedLoader.PlanAsync"/>.
/// </summary>
public static class SeedPreflight
{
    private static readonly string[] FirmClearable = ["clientSegmentId", "attributesJson", "notes"];
    private static readonly string[] RelationshipClearable = ["acquisitionDate", "endDate", "pricePaidPerPeriod", "servedProcessIds"];

    public static List<string> Check(SeedFile seed)
    {
        var problems = new List<string>();
        var dynamics = seed.MarketDynamics;
        if (dynamics is null) return problems;

        var defaults = dynamics.SourceDefaults;
        var defined = new Dictionary<SourceRef, string>();

        void Define(SeedSourceRef? source, string where)
        {
            if (source is null) return;
            try
            {
                var resolved = Resolve(source, defaults);
                if (!defined.TryAdd(resolved, where))
                    problems.Add($"{where}: source record {Describe(resolved)} appears more than once in the file " +
                                 $"(also {defined[resolved]}). One source record is one row.");
            }
            catch (ArgumentException ex)
            {
                problems.Add($"{where}: {ex.Message}");
            }
        }

        void Refer(SeedSourceRef? source, string where)
        {
            if (source is null) return;
            try { Resolve(source, defaults); }
            catch (ArgumentException ex) { problems.Add($"{where}: {ex.Message}"); }
        }

        foreach (var firm in dynamics.Firms)
        {
            var where = $"firm '{firm.Name}'";
            Define(firm.Source, where);
            CheckClears(firm.ClearFields, firm.Source is not null, FirmClearable, where, problems, new()
            {
                ["clientSegmentId"] = firm.SegmentName is not null,
                ["attributesJson"] = firm.AttributesJson is not null,
                ["notes"] = firm.Notes is not null
            });

            foreach (var relationship in firm.Relationships)
            {
                var edge = $"{where} relationship '{relationship.ProductName}'";
                foreach (var source in relationship.Sources) Define(source, edge);
                CheckClears(relationship.ClearFields, relationship.Sources.Count > 0, RelationshipClearable, edge, problems, new()
                {
                    ["acquisitionDate"] = relationship.AcquisitionDate is not null,
                    ["endDate"] = relationship.EndDate is not null,
                    ["pricePaidPerPeriod"] = false, // not settable from a seed file
                    ["servedProcessIds"] = relationship.ServedProcessNames.Count > 0
                });
            }

            foreach (var group in firm.Relationships.GroupBy(r => r.ProductName.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add($"{where}: product '{group.Key}' appears on {group.Count()} relationship rows. The model keeps one " +
                             "edge per firm and product; consolidate its source records deliberately by listing them all in one " +
                             "row's sources, or raise the separate episodes as an exception for review.");
        }

        var duplicateNames = dynamics.Firms.GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        void FirmReference(string? firmName, SeedSourceRef? firmSource, string where, bool required)
        {
            Refer(firmSource, $"{where} firmSource");
            if (firmSource is not null) return;
            if (string.IsNullOrWhiteSpace(firmName))
            {
                if (required) problems.Add($"{where}: give firmName or firmSource.");
                return;
            }
            if (duplicateNames.Contains(firmName.Trim()))
                problems.Add($"{where}: firmName '{firmName}' names more than one firm in this file; reference it by firmSource.");
        }

        foreach (var opportunity in dynamics.Opportunities)
        {
            var where = $"opportunity {SeedLoader.OpportunityKey(opportunity)}";
            Define(opportunity.Source, where);
            FirmReference(opportunity.FirmName, opportunity.FirmSource, where, required: true);
        }

        foreach (var churn in dynamics.ChurnEvents)
        {
            var where = $"churn event {SeedLoader.FactKey(churn.SegmentName, churn.EffectiveDate, churn.Source)}";
            Define(churn.Source, where);
            FirmReference(churn.FirmName, churn.FirmSource, where, required: false);
        }

        foreach (var arrival in dynamics.ArrivalCounts)
            Define(arrival.Source, $"arrival count {SeedLoader.FactKey(arrival.SegmentName, arrival.Month, arrival.Source)}");

        return problems;
    }

    /// <summary>The full identity of a seed row's source record, filling system/instance from the file's defaults.</summary>
    public static SourceRef Resolve(SeedSourceRef source, SeedSourceDefaults? defaults)
    {
        string Part(string? value, string? fallback, string name)
        {
            var part = string.IsNullOrEmpty(value) ? fallback : value;
            if (string.IsNullOrEmpty(part))
                throw new ArgumentException($"source {name} is missing (set it on the row or in marketDynamics.sourceDefaults).");
            if (part.Length > 200)
                throw new ArgumentException($"source {name} '{part[..20]}...' is longer than 200 characters.");
            return part;
        }

        return new SourceRef(
            Part(source.SourceSystem, defaults?.SourceSystem, "sourceSystem"),
            Part(source.SourceInstance, defaults?.SourceInstance, "sourceInstance"),
            Part(source.ObjectKind, null, "objectKind"),
            Part(source.SourceRecordId, null, "sourceRecordId"));
    }

    public static string Describe(SourceRef source) =>
        $"{source.SourceSystem}/{source.SourceInstance}/{source.ObjectKind}/{source.SourceRecordId}";

    private static void CheckClears(List<string> clears, bool sourced, string[] clearable, string where,
        List<string> problems, Dictionary<string, bool> supplied)
    {
        if (clears.Count == 0) return;
        if (!sourced)
        {
            problems.Add($"{where}: clearFields applies only to a row with source identity; a legacy row is overwritten whole.");
            return;
        }
        foreach (var field in clears)
        {
            if (!clearable.Contains(field))
                problems.Add($"{where}: '{field}' cannot be cleared (clearable: {string.Join(", ", clearable)}).");
            else if (supplied[field])
                problems.Add($"{where}: '{field}' is both supplied and cleared.");
        }
    }
}
