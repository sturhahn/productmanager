using ProductManager.Cli.Client;
using ProductManager.Cli.Commands;

namespace ProductManager.Cli.Tests;

public class Phase3ParseHelpersTests
{
    [Theory]
    [InlineData("won", OpportunityStage.Won)]
    [InlineData("Won", OpportunityStage.Won)]
    [InlineData("WON", OpportunityStage.Won)]
    [InlineData("lost", OpportunityStage.Lost)]
    [InlineData("no-decision", OpportunityStage.NoDecision)]
    [InlineData("No-Decision", OpportunityStage.NoDecision)]
    [InlineData("nodecision", OpportunityStage.NoDecision)]
    public void ParseOpportunityOutcome_accepts_the_documented_spellings(string input, OpportunityStage expected)
    {
        Assert.Equal(expected, Phase3ParseHelpers.ParseOpportunityOutcome(input));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("qualified")]
    [InlineData("won-ish")]
    [InlineData("")]
    public void ParseOpportunityOutcome_rejects_anything_else(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => Phase3ParseHelpers.ParseOpportunityOutcome(input));
        Assert.Contains("--outcome", ex.Message);
    }

    [Theory]
    [InlineData("aggregate", SimulationEngineMode.Aggregate)]
    [InlineData("Aggregate", SimulationEngineMode.Aggregate)]
    [InlineData("firm-level", SimulationEngineMode.FirmLevel)]
    [InlineData("Firm-Level", SimulationEngineMode.FirmLevel)]
    [InlineData("firmlevel", SimulationEngineMode.FirmLevel)]
    public void ParseEngineMode_accepts_the_documented_spellings(string input, SimulationEngineMode expected)
    {
        Assert.Equal(expected, Phase3ParseHelpers.ParseEngineMode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseEngineMode_defaults_to_Aggregate_when_not_given(string? input)
    {
        Assert.Equal(SimulationEngineMode.Aggregate, Phase3ParseHelpers.ParseEngineMode(input));
    }

    [Fact]
    public void ParseEngineMode_rejects_an_unrecognized_value()
    {
        var ex = Assert.Throws<ArgumentException>(() => Phase3ParseHelpers.ParseEngineMode("firm"));
        Assert.Contains("--mode", ex.Message);
    }
}
