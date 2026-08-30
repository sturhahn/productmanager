using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Tests;

/// <summary>Pins the CLI's mirror of the API's ChoiceParameterKeys scheme — a mismatch here means a
/// seeded choice parameter would land under a key the firm-level engine never reads.</summary>
public class ChoiceParameterKeysTests
{
    private static readonly Guid SegmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProcessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProductId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("SwitchingCostBase", "choice.switching-cost-base")]
    [InlineData("WorkaroundCostWeight", "choice.workaround-cost-weight")]
    [InlineData("ExpansionPropensity", "choice.expansion-propensity")]
    [InlineData("NoDecisionUtility", "choice.no-decision-utility")]
    [InlineData("IntegrationWeight", "choice.integration-weight")]
    [InlineData("PriceScale", "choice.price-scale")]
    [InlineData("SalesCyclePriorWeight", "choice.sales-cycle-prior-weight")]
    public void Global_scalar_kinds_build_the_documented_literal_key(string kind, string expectedKey)
    {
        Assert.Equal(expectedKey, ChoiceParameterKeys.Build(kind, null, null, null));
    }

    [Fact]
    public void PriceSensitivity_and_ScopeClusterCount_key_by_segment_only()
    {
        Assert.Equal($"choice.price-sensitivity:{SegmentId}", ChoiceParameterKeys.Build("PriceSensitivity", SegmentId, null, null));
        Assert.Equal($"choice.scope-cluster-count:{SegmentId}", ChoiceParameterKeys.Build("ScopeClusterCount", SegmentId, null, null));
    }

    [Fact]
    public void SalesCycleMu_and_Sigma_omit_the_product_segment_when_no_product_is_given()
    {
        Assert.Equal($"choice.sales-cycle-mu:{SegmentId}", ChoiceParameterKeys.Build("SalesCycleMu", SegmentId, null, null));
        Assert.Equal($"choice.sales-cycle-sigma:{SegmentId}", ChoiceParameterKeys.Build("SalesCycleSigma", SegmentId, null, null));
    }

    [Fact]
    public void SalesCycleMu_and_Sigma_append_the_product_when_one_is_given()
    {
        Assert.Equal($"choice.sales-cycle-mu:{SegmentId}:{ProductId}", ChoiceParameterKeys.Build("SalesCycleMu", SegmentId, null, ProductId));
        Assert.Equal($"choice.sales-cycle-sigma:{SegmentId}:{ProductId}", ChoiceParameterKeys.Build("SalesCycleSigma", SegmentId, null, ProductId));
    }

    [Fact]
    public void ImportanceWeight_keys_by_segment_then_process()
    {
        Assert.Equal($"utility.importance-weight:{SegmentId}:{ProcessId}", ChoiceParameterKeys.Build("ImportanceWeight", SegmentId, ProcessId, null));
    }

    [Fact]
    public void BrandBias_keys_by_product_then_segment()
    {
        Assert.Equal($"utility.brand-bias:{ProductId}:{SegmentId}", ChoiceParameterKeys.Build("BrandBias", SegmentId, null, ProductId));
    }

    [Theory]
    [InlineData("PriceSensitivity")]
    [InlineData("ScopeClusterCount")]
    [InlineData("SalesCycleMu")]
    [InlineData("SalesCycleSigma")]
    public void Segment_keyed_kinds_reject_a_missing_segment(string kind)
    {
        var ex = Assert.Throws<ArgumentException>(() => ChoiceParameterKeys.Build(kind, null, null, null));
        Assert.Contains("segmentName", ex.Message);
    }

    [Fact]
    public void ImportanceWeight_rejects_a_missing_process()
    {
        var ex = Assert.Throws<ArgumentException>(() => ChoiceParameterKeys.Build("ImportanceWeight", SegmentId, null, null));
        Assert.Contains("businessProcessName", ex.Message);
    }

    [Fact]
    public void BrandBias_rejects_a_missing_product()
    {
        var ex = Assert.Throws<ArgumentException>(() => ChoiceParameterKeys.Build("BrandBias", SegmentId, null, null));
        Assert.Contains("moduleProductName", ex.Message);
    }

    [Fact]
    public void An_unrecognized_kind_is_rejected_with_the_allowed_vocabulary_listed()
    {
        var ex = Assert.Throws<ArgumentException>(() => ChoiceParameterKeys.Build("NotAThing", SegmentId, ProcessId, ProductId));
        Assert.Contains("NotAThing", ex.Message);
        Assert.Contains("SwitchingCostBase", ex.Message);
    }

    [Fact]
    public void Kinds_vocabulary_has_no_duplicates_and_every_kind_builds_successfully_given_all_three_ids()
    {
        Assert.Equal(ChoiceParameterKeys.Kinds.Count, ChoiceParameterKeys.Kinds.Distinct().Count());

        foreach (var kind in ChoiceParameterKeys.Kinds)
        {
            var key = ChoiceParameterKeys.Build(kind, SegmentId, ProcessId, ProductId);
            Assert.False(string.IsNullOrWhiteSpace(key));
        }
    }
}
