using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>
/// Small CLI-friendly parsers for Phase 3 option spellings that read better on a command line than
/// the wire enum's exact member names (e.g. "no-decision" vs "NoDecision"). Public — like
/// <see cref="ParameterKeyHelper"/> and <see cref="ParseHelpers"/> — so they are unit-testable
/// directly without exposing the rest of the command-wiring internals.
/// </summary>
public static class Phase3ParseHelpers
{
    /// <summary>Accepts "won" / "lost" / "no-decision" (any case), the friendlier spelling
    /// 'opportunity decide --outcome' takes, and maps it onto the wire's OpportunityStage.</summary>
    public static OpportunityStage ParseOpportunityOutcome(string value) => value.Trim().ToLowerInvariant() switch
    {
        "won" => OpportunityStage.Won,
        "lost" => OpportunityStage.Lost,
        "no-decision" or "nodecision" => OpportunityStage.NoDecision,
        _ => throw new ArgumentException($"'{value}' is not a valid --outcome (expected one of: won, lost, no-decision).")
    };

    /// <summary>Accepts "aggregate" / "firm-level" (any case); null or blank defaults to Aggregate,
    /// the same default 'sim run --mode' documents.</summary>
    public static SimulationEngineMode ParseEngineMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SimulationEngineMode.Aggregate;
        return value.Trim().ToLowerInvariant() switch
        {
            "aggregate" => SimulationEngineMode.Aggregate,
            "firm-level" or "firmlevel" => SimulationEngineMode.FirmLevel,
            _ => throw new ArgumentException($"'{value}' is not a valid --mode (expected one of: aggregate, firm-level).")
        };
    }
}
