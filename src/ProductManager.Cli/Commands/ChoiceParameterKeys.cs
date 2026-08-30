namespace ProductManager.Cli.Commands;

/// <summary>
/// Client-side mirror of <c>ProductManager.Api.Simulation.ChoiceParameterKeys</c> — the free-form
/// <c>Parameter.Key</c> namespace the firm-level choice model reads its knobs from (switching cost,
/// sales-cycle length, per-process importance weight, brand bias), since those are not flows and so
/// cannot use the engine's structural (segment, flow kind) keying. Kept as an independent copy
/// (rather than a project reference to the API) so the CLI stays a pure HTTP client — see
/// <see cref="ProductManager.Cli.Client.Enums"/>'s header for the same rationale.
/// Used by <c>ProductManager.Cli.Seed.SeedLoader</c> to build the key for a seed file's choice
/// parameters after resolving their segment/process/product names to ids.
/// </summary>
public static class ChoiceParameterKeys
{
    public const string SwitchingCostBase = "choice.switching-cost-base";
    public const string WorkaroundCostWeight = "choice.workaround-cost-weight";
    public const string ExpansionPropensity = "choice.expansion-propensity";
    public const string NoDecisionUtility = "choice.no-decision-utility";
    public const string IntegrationWeight = "choice.integration-weight";
    public const string PriceScale = "choice.price-scale";
    public const string SalesCyclePriorWeight = "choice.sales-cycle-prior-weight";

    private const string PriceSensitivityPrefix = "choice.price-sensitivity:";
    private const string ScopeClusterCountPrefix = "choice.scope-cluster-count:";
    private const string SalesCycleMuPrefix = "choice.sales-cycle-mu:";
    private const string SalesCycleSigmaPrefix = "choice.sales-cycle-sigma:";
    private const string ImportanceWeightPrefix = "utility.importance-weight:";
    private const string BrandBiasPrefix = "utility.brand-bias:";

    public static string PriceSensitivity(Guid segmentId) => $"{PriceSensitivityPrefix}{segmentId}";

    public static string ScopeClusterCount(Guid segmentId) => $"{ScopeClusterCountPrefix}{segmentId}";

    public static string SalesCycleMu(Guid segmentId, Guid? productId) =>
        productId is null ? $"{SalesCycleMuPrefix}{segmentId}" : $"{SalesCycleMuPrefix}{segmentId}:{productId}";

    public static string SalesCycleSigma(Guid segmentId, Guid? productId) =>
        productId is null ? $"{SalesCycleSigmaPrefix}{segmentId}" : $"{SalesCycleSigmaPrefix}{segmentId}:{productId}";

    public static string ImportanceWeight(Guid segmentId, Guid businessProcessId) =>
        $"{ImportanceWeightPrefix}{segmentId}:{businessProcessId}";

    public static string BrandBias(Guid productId, Guid segmentId) => $"{BrandBiasPrefix}{productId}:{segmentId}";

    /// <summary>The vocabulary a seed file's <c>choiceKind</c> field may name.</summary>
    public static readonly IReadOnlyList<string> Kinds =
    [
        "SwitchingCostBase", "WorkaroundCostWeight", "ExpansionPropensity", "NoDecisionUtility",
        "IntegrationWeight", "PriceScale", "SalesCyclePriorWeight", "PriceSensitivity",
        "ScopeClusterCount", "SalesCycleMu", "SalesCycleSigma", "ImportanceWeight", "BrandBias"
    ];

    /// <summary>
    /// Builds the free-form key for one choice parameter. <paramref name="segmentId"/>/
    /// <paramref name="processId"/>/<paramref name="productId"/> are required exactly where the
    /// corresponding key needs them — see <see cref="Kinds"/> for the vocabulary and
    /// ChoiceParameterKeys's own XML doc on the API side for what each key drives.
    /// </summary>
    public static string Build(string choiceKind, Guid? segmentId, Guid? processId, Guid? productId) => choiceKind switch
    {
        "SwitchingCostBase" => SwitchingCostBase,
        "WorkaroundCostWeight" => WorkaroundCostWeight,
        "ExpansionPropensity" => ExpansionPropensity,
        "NoDecisionUtility" => NoDecisionUtility,
        "IntegrationWeight" => IntegrationWeight,
        "PriceScale" => PriceScale,
        "SalesCyclePriorWeight" => SalesCyclePriorWeight,
        "PriceSensitivity" => PriceSensitivity(Require(segmentId, "segmentName", choiceKind)),
        "ScopeClusterCount" => ScopeClusterCount(Require(segmentId, "segmentName", choiceKind)),
        "SalesCycleMu" => SalesCycleMu(Require(segmentId, "segmentName", choiceKind), productId),
        "SalesCycleSigma" => SalesCycleSigma(Require(segmentId, "segmentName", choiceKind), productId),
        "ImportanceWeight" => ImportanceWeight(Require(segmentId, "segmentName", choiceKind), Require(processId, "businessProcessName", choiceKind)),
        "BrandBias" => BrandBias(Require(productId, "moduleProductName", choiceKind), Require(segmentId, "segmentName", choiceKind)),
        _ => throw new ArgumentException(
            $"'{choiceKind}' is not a recognized choiceKind (expected one of: {string.Join(", ", Kinds)}).")
    };

    private static Guid Require(Guid? value, string fieldName, string choiceKind) =>
        value ?? throw new ArgumentException($"parameter choiceKind '{choiceKind}' requires '{fieldName}'.");
}
