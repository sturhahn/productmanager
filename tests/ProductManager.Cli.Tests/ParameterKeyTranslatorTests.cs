using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Tests;

public class ParameterKeyTranslatorTests
{
    private static readonly Guid SegmentId = Guid.Parse("34e4d3bf-0475-462d-afc1-9fe96168296e");
    private static readonly Guid ModuleId = Guid.Parse("6befced3-94dd-4c33-8990-10073b0c97f9");
    private static readonly Guid ProcessId = Guid.Parse("cec3eedf-c83a-487f-9c3a-42d71775d02c");

    private static readonly ParameterNameMaps Names = new(
        Segments: new Dictionary<Guid, string> { [SegmentId] = "Boutique Family Offices" },
        Products: new Dictionary<Guid, string> { [ModuleId] = "Meridian Reporting Module" },
        BusinessProcesses: new Dictionary<Guid, string> { [ProcessId] = "Client Onboarding" });

    // ---------- Engine parameters: "{FlowKind}:{segmentId}[:{moduleProductId}]" ----------

    [Fact]
    public void Translates_a_segment_only_engine_key()
    {
        var text = ParameterKeyTranslator.Translate($"ChurnHazard:{SegmentId}", Names);
        Assert.Equal("Churn hazard — Boutique Family Offices", text);
    }

    [Fact]
    public void Translates_a_segment_and_module_engine_key()
    {
        var text = ParameterKeyTranslator.Translate($"CrossSellRate:{SegmentId}:{ModuleId}", Names);
        Assert.Equal("Cross-sell — Boutique Family Offices — Meridian Reporting Module", text);
    }

    [Theory]
    [InlineData("NewFirmArrival", "New firm arrival")]
    [InlineData("ConversionToMarket", "Conversion to market")]
    [InlineData("WinRate", "Win rate")]
    public void Translates_every_flow_kind_to_its_own_label(string flowKind, string expectedLabel)
    {
        var text = ParameterKeyTranslator.Translate($"{flowKind}:{SegmentId}", Names);
        Assert.Equal($"{expectedLabel} — Boutique Family Offices", text);
    }

    // ---------- Free-form choice parameters ----------

    [Fact]
    public void Translates_a_scalar_choice_parameter_with_no_ids()
    {
        Assert.Equal("Switching cost base", ParameterKeyTranslator.Translate(ChoiceParameterKeys.SwitchingCostBase, Names));
        Assert.Equal("Expansion propensity", ParameterKeyTranslator.Translate(ChoiceParameterKeys.ExpansionPropensity, Names));
    }

    [Fact]
    public void Translates_a_segment_only_choice_parameter()
    {
        var key = ChoiceParameterKeys.PriceSensitivity(SegmentId);
        Assert.Equal("Price sensitivity — Boutique Family Offices", ParameterKeyTranslator.Translate(key, Names));
    }

    [Fact]
    public void Translates_sales_cycle_mu_without_a_product()
    {
        var key = ChoiceParameterKeys.SalesCycleMu(SegmentId, productId: null);
        Assert.Equal("Sales cycle mu — Boutique Family Offices", ParameterKeyTranslator.Translate(key, Names));
    }

    [Fact]
    public void Translates_sales_cycle_mu_with_a_product()
    {
        var key = ChoiceParameterKeys.SalesCycleMu(SegmentId, ModuleId);
        Assert.Equal("Sales cycle mu — Boutique Family Offices — Meridian Reporting Module", ParameterKeyTranslator.Translate(key, Names));
    }

    [Fact]
    public void Translates_sales_cycle_sigma_with_a_product()
    {
        var key = ChoiceParameterKeys.SalesCycleSigma(SegmentId, ModuleId);
        Assert.Equal("Sales cycle sigma — Boutique Family Offices — Meridian Reporting Module", ParameterKeyTranslator.Translate(key, Names));
    }

    [Fact]
    public void Translates_importance_weight_segment_and_business_process()
    {
        var key = ChoiceParameterKeys.ImportanceWeight(SegmentId, ProcessId);
        Assert.Equal("Importance weight — Boutique Family Offices — Client Onboarding", ParameterKeyTranslator.Translate(key, Names));
    }

    [Fact]
    public void Translates_brand_bias_product_then_segment()
    {
        var key = ChoiceParameterKeys.BrandBias(ModuleId, SegmentId);
        Assert.Equal("Brand bias — Meridian Reporting Module — Boutique Family Offices", ParameterKeyTranslator.Translate(key, Names));
    }

    // ---------- Missing-name fallback ----------

    [Fact]
    public void Falls_back_to_a_short_guid_when_the_segment_name_is_missing()
    {
        var unknownSegment = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var text = ParameterKeyTranslator.Translate($"ChurnHazard:{unknownSegment}", Names);

        Assert.Equal("Churn hazard — (11111111…)", text);
    }

    [Fact]
    public void Falls_back_to_a_short_guid_when_the_module_name_is_missing()
    {
        var unknownModule = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var text = ParameterKeyTranslator.Translate($"CrossSellRate:{SegmentId}:{unknownModule}", Names);

        Assert.Equal("Cross-sell — Boutique Family Offices — (99999999…)", text);
    }

    [Fact]
    public void Falls_back_to_the_short_guid_when_no_names_are_known_at_all()
    {
        var text = ParameterKeyTranslator.Translate($"ChurnHazard:{SegmentId}", ParameterNameMaps.Empty);
        Assert.Equal($"Churn hazard — ({SegmentId.ToString()[..8]}…)", text);
    }

    // ---------- Unrecognized shapes pass through unchanged ----------

    [Fact]
    public void Passes_through_a_free_form_user_supplied_key_unchanged()
    {
        Assert.Equal("my-custom-key", ParameterKeyTranslator.Translate("my-custom-key", Names));
    }

    [Fact]
    public void Passes_through_a_key_whose_first_segment_is_not_a_known_flow_kind()
    {
        var text = ParameterKeyTranslator.Translate($"NotARealFlowKind:{SegmentId}", Names);
        Assert.Equal($"NotARealFlowKind:{SegmentId}", text);
    }
}
