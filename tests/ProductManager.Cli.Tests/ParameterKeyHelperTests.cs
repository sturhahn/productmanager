using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Tests;

public class ParameterKeyHelperTests
{
    private static readonly Guid SegmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ModuleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void IsEngineParameter_is_true_when_either_segment_or_flow_kind_is_given()
    {
        Assert.True(ParameterKeyHelper.IsEngineParameter(SegmentId, null));
        Assert.True(ParameterKeyHelper.IsEngineParameter(null, SimulationFlowKind.WinRate));
        Assert.True(ParameterKeyHelper.IsEngineParameter(SegmentId, SimulationFlowKind.WinRate));
    }

    [Fact]
    public void IsEngineParameter_is_false_when_neither_is_given()
    {
        Assert.False(ParameterKeyHelper.IsEngineParameter(null, null));
    }

    [Fact]
    public void Validate_accepts_a_complete_engine_key()
    {
        var exception = Record.Exception(() => ParameterKeyHelper.Validate(SegmentId, SimulationFlowKind.ChurnHazard, null));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_accepts_a_free_form_key_when_neither_segment_nor_flow_is_given()
    {
        var exception = Record.Exception(() => ParameterKeyHelper.Validate(null, null, "some-free-form-key"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Validate_rejects_a_half_specified_engine_key(bool haveSegment, bool haveFlow)
    {
        Guid? segmentId = haveSegment ? SegmentId : null;
        SimulationFlowKind? flowKind = haveFlow ? SimulationFlowKind.WinRate : null;

        var ex = Assert.Throws<ArgumentException>(() => ParameterKeyHelper.Validate(segmentId, flowKind, null));
        Assert.Contains("--segment-id", ex.Message);
        Assert.Contains("--flow-kind", ex.Message);
    }

    [Fact]
    public void Validate_rejects_neither_an_engine_key_nor_a_free_form_key()
    {
        var ex = Assert.Throws<ArgumentException>(() => ParameterKeyHelper.Validate(null, null, null));
        Assert.Contains("--key", ex.Message);
    }

    [Fact]
    public void Validate_rejects_a_whitespace_only_key_the_same_as_a_missing_one()
    {
        Assert.Throws<ArgumentException>(() => ParameterKeyHelper.Validate(null, null, "   "));
    }

    [Fact]
    public void DescribeTarget_formats_an_engine_key_with_the_optional_module_suffix()
    {
        var withoutModule = ParameterKeyHelper.DescribeTarget(SegmentId, SimulationFlowKind.WinRate, null, null);
        Assert.Equal($"segment {SegmentId}, flow WinRate", withoutModule);

        var withModule = ParameterKeyHelper.DescribeTarget(SegmentId, SimulationFlowKind.CrossSellRate, ModuleId, null);
        Assert.Equal($"segment {SegmentId}, flow CrossSellRate, module {ModuleId}", withModule);
    }

    [Fact]
    public void DescribeTarget_formats_a_free_form_key()
    {
        Assert.Equal("key 'discount-scenario-a'", ParameterKeyHelper.DescribeTarget(null, null, null, "discount-scenario-a"));
    }
}
