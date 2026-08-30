using ProductManager.Domain.Common;
using ProductManager.Domain.Simulation;

namespace ProductManager.Api.Simulation;

/// <summary>
/// The key scheme for the firm-level choice model's parameters inside an <see cref="AssumptionSet"/>.
///
/// <para>
/// Engine v0's parameters are keyed structurally — (segment, flow kind, module?) — because they are
/// *flows*, and the engine reads them by that triple. The choice model's knobs are not flows: a
/// switching cost, a sales-cycle length, a per-process importance weight. Rather than bend the flow
/// keying (which would mean inventing fake flow kinds) they use the existing free-form
/// <see cref="Parameter.Key"/> channel with a namespaced, machine-parseable convention:
/// </para>
///
/// <list type="table">
/// <item><term><c>choice.switching-cost-base</c></term><description>ChoiceParameters.SwitchingCostBase</description></item>
/// <item><term><c>choice.workaround-cost-weight</c></term><description>ChoiceParameters.WorkaroundCostWeight</description></item>
/// <item><term><c>choice.expansion-propensity</c></term><description>ChoiceParameters.ExpansionPropensity</description></item>
/// <item><term><c>choice.no-decision-utility</c></term><description>ChoiceParameters.NoDecisionUtility</description></item>
/// <item><term><c>choice.integration-weight</c></term><description>ChoiceParameters.IntegrationWeight</description></item>
/// <item><term><c>choice.price-scale</c></term><description>ChoiceParameters.PriceScale</description></item>
/// <item><term><c>choice.price-sensitivity:{segmentId}</c></term><description>FirmLevelSegmentInput.PriceSensitivity (whole distribution)</description></item>
/// <item><term><c>choice.scope-cluster-count:{segmentId}</c></term><description>OpportunityScoping.ScopeClusterCount (whole distribution)</description></item>
/// <item><term><c>choice.sales-cycle-mu:{segmentId}[:{productId}]</c></term><description>SalesCycleSpec.Mu</description></item>
/// <item><term><c>choice.sales-cycle-sigma:{segmentId}[:{productId}]</c></term><description>SalesCycleSpec.Sigma</description></item>
/// <item><term><c>choice.sales-cycle-prior-weight</c></term><description>calibration prior strength of the sales-cycle blend</description></item>
/// <item><term><c>utility.importance-weight:{segmentId}:{processId}</c></term><description>ProcessNeed.ImportanceWeight (whole distribution; overrides the Need's point weight)</description></item>
/// <item><term><c>utility.brand-bias:{productId}:{segmentId}</c></term><description>BrandBias.Bias</description></item>
/// </list>
///
/// <para>
/// Scalar knobs read the distribution's <em>mean</em>, so an asserted point value and a fitted
/// distribution can occupy the same key. Distribution-valued knobs (price sensitivity, scope
/// cluster count, importance weight) are passed to the engine whole, because the engine samples
/// them per Monte Carlo path — that is where parameter uncertainty enters the choice model.
/// Anything absent falls back to the documented default below, so a half-specified assumption set
/// still runs.
/// </para>
///
/// <para>
/// Segment tendencies that are observations about the market rather than model knobs stay on the
/// entities where Phase 1/2 put them: <c>ClientSegment.SuiteAffinity</c> is the broad-scope
/// probability, <c>ClientSegment.ConsolidationPreference</c> the consolidation bonus,
/// <c>Need</c> supplies which processes matter and hard/soft + workaround cost, and
/// <c>Channel</c> supplies reach, origination and product bias.
/// </para>
/// </summary>
public static class ChoiceParameterKeys
{
    // ---- Global scalars ----

    public const string SwitchingCostBase = "choice.switching-cost-base";
    public const string WorkaroundCostWeight = "choice.workaround-cost-weight";
    public const string ExpansionPropensity = "choice.expansion-propensity";
    public const string NoDecisionUtility = "choice.no-decision-utility";
    public const string IntegrationWeight = "choice.integration-weight";
    public const string PriceScale = "choice.price-scale";
    public const string SalesCyclePriorWeight = "choice.sales-cycle-prior-weight";

    // ---- Prefixes for the keyed families ----

    public const string PriceSensitivityPrefix = "choice.price-sensitivity:";
    public const string ScopeClusterCountPrefix = "choice.scope-cluster-count:";
    public const string SalesCycleMuPrefix = "choice.sales-cycle-mu:";
    public const string SalesCycleSigmaPrefix = "choice.sales-cycle-sigma:";
    public const string ImportanceWeightPrefix = "utility.importance-weight:";
    public const string BrandBiasPrefix = "utility.brand-bias:";

    // ---- Defaults when a key is absent ----

    /// <summary>No switching friction asserted means none applied — the neutral prior, not a guess.</summary>
    public const double DefaultSwitchingCostBase = 0.0;

    public const double DefaultWorkaroundCostWeight = 1.0;

    /// <summary>Land-and-expand is off until somebody asserts a propensity for it.</summary>
    public const double DefaultExpansionPropensity = 0.0;

    public const double DefaultNoDecisionUtility = 0.0;

    /// <summary>Matches the engine contract's own default.</summary>
    public const double DefaultIntegrationWeight = 1.0;

    /// <summary>Matches the engine contract's own default.</summary>
    public const double DefaultPriceScale = 1000.0;

    /// <summary>Price sensitivity of a segment nobody has estimated: one unit of utility per PriceScale of price.</summary>
    public const double DefaultPriceSensitivity = 1.0;

    /// <summary>A narrow-scope opportunity buys one cluster unless the assumption set says otherwise.</summary>
    public const double DefaultScopeClusterCount = 1.0;

    /// <summary>Prior strength (in pseudo-observations) of a sales-cycle prior during calibration.</summary>
    public const double DefaultSalesCyclePriorWeight = 10.0;

    public static string PriceSensitivity(Guid segmentId) => $"{PriceSensitivityPrefix}{segmentId}";

    public static string ScopeClusterCount(Guid segmentId) => $"{ScopeClusterCountPrefix}{segmentId}";

    public static string SalesCycleMu(Guid segmentId, Guid? productId) =>
        productId is null ? $"{SalesCycleMuPrefix}{segmentId}" : $"{SalesCycleMuPrefix}{segmentId}:{productId}";

    public static string SalesCycleSigma(Guid segmentId, Guid? productId) =>
        productId is null ? $"{SalesCycleSigmaPrefix}{segmentId}" : $"{SalesCycleSigmaPrefix}{segmentId}:{productId}";

    public static string ImportanceWeight(Guid segmentId, Guid businessProcessId) =>
        $"{ImportanceWeightPrefix}{segmentId}:{businessProcessId}";

    public static string BrandBias(Guid productId, Guid segmentId) => $"{BrandBiasPrefix}{productId}:{segmentId}";

    /// <summary>
    /// Reads the (segment, product?) a sales-cycle key names. False for anything that is not one of
    /// those keys — a free-form assumption keyed by hand is documentation, not engine input.
    /// </summary>
    public static bool TryParseSalesCycleKey(string key, string prefix, out Guid segmentId, out Guid? productId)
    {
        segmentId = Guid.Empty;
        productId = null;
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var parts = key[prefix.Length..].Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (1 or 2)) return false;
        if (!Guid.TryParse(parts[0], out segmentId)) return false;
        if (parts.Length == 2)
        {
            if (!Guid.TryParse(parts[1], out var product)) return false;
            productId = product;
        }

        return true;
    }

    /// <summary>
    /// The scalar a knob-shaped parameter contributes: the distribution's mean. A point value is
    /// its own mean, so asserted knobs behave exactly as written, while a fitted distribution still
    /// has something sensible to contribute to a scalar slot.
    /// </summary>
    public static double Mean(Distribution distribution) => distribution.Type switch
    {
        DistributionType.Point => distribution.ParamA,
        DistributionType.Normal => distribution.ParamA,
        DistributionType.Lognormal => Math.Exp(distribution.ParamA + distribution.ParamB * distribution.ParamB / 2.0),
        DistributionType.Beta => distribution.ParamA / (distribution.ParamA + distribution.ParamB),
        _ => distribution.ParamA
    };
}
