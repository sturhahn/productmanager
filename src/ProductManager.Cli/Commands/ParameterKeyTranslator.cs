using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>The name maps a <see cref="ParameterKeyTranslator"/> needs — a small bundle so call
/// sites don't have to thread three separate dictionaries through. See <see cref="NameResolver.ParameterMaps"/>
/// for the one built from the live API; tests can construct this directly with fake maps.</summary>
public sealed record ParameterNameMaps(
    IReadOnlyDictionary<Guid, string> Segments,
    IReadOnlyDictionary<Guid, string> Products,
    IReadOnlyDictionary<Guid, string> BusinessProcesses)
{
    public static readonly ParameterNameMaps Empty = new(
        new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, string>());
}

/// <summary>
/// Pure translation of a raw <c>AssumptionSet</c> Parameter.Key into a human-readable label, given
/// name maps for segments/products/business-processes. No I/O, so it is unit-testable directly with
/// a fake <see cref="ParameterNameMaps"/> — used by 'calibrate run'/'diff' to translate the
/// calibration diff table's parameter column instead of printing the raw key.
///
/// Mirrors the two key shapes the API actually produces (see
/// <c>ProductManager.Domain.Simulation.Parameter.EngineKey</c> and
/// <see cref="ChoiceParameterKeys"/> — this class is the client-side inverse of both):
///   - An *engine* parameter: "{FlowKind}:{segmentId}" or "{FlowKind}:{segmentId}:{moduleProductId}".
///   - A *free-form choice* parameter: "choice.&lt;kind&gt;" or "choice.&lt;kind&gt;:{segmentId}"
///     or "choice.&lt;kind&gt;:{segmentId}:{productId}", or "utility.importance-weight:{segmentId}:{businessProcessId}",
///     or "utility.brand-bias:{productId}:{segmentId}".
/// A key that doesn't match any recognized shape (a user-supplied free-form --key, or a kind this
/// translator doesn't know about yet) is passed through unchanged — this is a display nicety, never
/// a hard requirement to understand every key that could theoretically exist.
/// </summary>
public static class ParameterKeyTranslator
{
    private static readonly IReadOnlyDictionary<string, string> ScalarChoiceLabels = new Dictionary<string, string>
    {
        [ChoiceParameterKeys.SwitchingCostBase] = "Switching cost base",
        [ChoiceParameterKeys.WorkaroundCostWeight] = "Workaround cost weight",
        [ChoiceParameterKeys.ExpansionPropensity] = "Expansion propensity",
        [ChoiceParameterKeys.NoDecisionUtility] = "No-decision utility",
        [ChoiceParameterKeys.IntegrationWeight] = "Integration weight",
        [ChoiceParameterKeys.PriceScale] = "Price scale",
        [ChoiceParameterKeys.SalesCyclePriorWeight] = "Sales cycle prior weight",
    };

    /// <summary>Translates one parameter key. Falls back to a short-guid form for any id the given
    /// maps don't cover (see <see cref="GuidDisplay"/>), and to the raw key for a shape it doesn't
    /// recognize at all.</summary>
    public static string Translate(string key, ParameterNameMaps names)
    {
        var parts = key.Split(':');
        var kind = parts[0];

        // Engine parameter: "{FlowKind}:{segmentId}" or "{FlowKind}:{segmentId}:{moduleProductId}".
        if (parts.Length is 2 or 3 && Enum.TryParse<SimulationFlowKind>(kind, out var flowKind) && Guid.TryParse(parts[1], out var segmentId))
        {
            var segment = GuidDisplay.Resolve(names.Segments, segmentId);
            if (parts.Length == 2)
                return $"{FlowLabel(flowKind)} — {segment}";

            var module = Guid.TryParse(parts[2], out var moduleId) ? GuidDisplay.Resolve(names.Products, moduleId) : parts[2];
            return $"{FlowLabel(flowKind)} — {segment} — {module}";
        }

        // Scalar free-form choice parameter — no ids at all.
        if (parts.Length == 1 && ScalarChoiceLabels.TryGetValue(kind, out var scalarLabel))
            return scalarLabel;

        // Segment-only free-form choice parameter: "{kind}:{segmentId}".
        if (parts.Length == 2 && Guid.TryParse(parts[1], out var segOnlyId))
        {
            var label = SegmentOnlyLabel(kind);
            if (label is not null)
                return $"{label} — {GuidDisplay.Resolve(names.Segments, segOnlyId)}";
        }

        if (parts.Length == 3)
        {
            // Segment + optional product: "choice.sales-cycle-{mu,sigma}:{segmentId}:{productId}".
            if (Guid.TryParse(parts[1], out var segId) && SegmentProductLabel(kind) is { } segProductLabel)
            {
                var segment = GuidDisplay.Resolve(names.Segments, segId);
                var product = Guid.TryParse(parts[2], out var productId) ? GuidDisplay.Resolve(names.Products, productId) : parts[2];
                return $"{segProductLabel} — {segment} — {product}";
            }

            // Segment + business process: "utility.importance-weight:{segmentId}:{businessProcessId}".
            if (kind == "utility.importance-weight" && Guid.TryParse(parts[1], out var iwSegmentId) && Guid.TryParse(parts[2], out var processId))
                return $"Importance weight — {GuidDisplay.Resolve(names.Segments, iwSegmentId)} — {GuidDisplay.Resolve(names.BusinessProcesses, processId)}";

            // Product + segment: "utility.brand-bias:{productId}:{segmentId}".
            if (kind == "utility.brand-bias" && Guid.TryParse(parts[1], out var productIdForBias) && Guid.TryParse(parts[2], out var biasSegmentId))
                return $"Brand bias — {GuidDisplay.Resolve(names.Products, productIdForBias)} — {GuidDisplay.Resolve(names.Segments, biasSegmentId)}";
        }

        return key;
    }

    private static string? SegmentOnlyLabel(string kind) => kind switch
    {
        "choice.price-sensitivity" => "Price sensitivity",
        "choice.scope-cluster-count" => "Scope cluster count",
        "choice.sales-cycle-mu" => "Sales cycle mu",
        "choice.sales-cycle-sigma" => "Sales cycle sigma",
        _ => null
    };

    private static string? SegmentProductLabel(string kind) => kind switch
    {
        "choice.sales-cycle-mu" => "Sales cycle mu",
        "choice.sales-cycle-sigma" => "Sales cycle sigma",
        _ => null
    };

    private static string FlowLabel(SimulationFlowKind kind) => kind switch
    {
        SimulationFlowKind.NewFirmArrival => "New firm arrival",
        SimulationFlowKind.ConversionToMarket => "Conversion to market",
        SimulationFlowKind.WinRate => "Win rate",
        SimulationFlowKind.ChurnHazard => "Churn hazard",
        SimulationFlowKind.CrossSellRate => "Cross-sell",
        _ => kind.ToString()
    };
}
