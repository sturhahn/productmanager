using ProductManager.Cli.Client;

namespace ProductManager.Cli.Commands;

/// <summary>
/// Pure client-side mirror of the validation <c>AssumptionSetsController.UpsertParameter</c> applies
/// server-side: a parameter is either an *engine* parameter (keyed by segment + flow kind, optionally
/// a module for CrossSellRate) or a *free-form* one (keyed by an arbitrary string). Running the same
/// check here lets 'assumptions set-param' fail fast with a clear message instead of a round trip.
/// </summary>
public static class ParameterKeyHelper
{
    public static bool IsEngineParameter(Guid? clientSegmentId, SimulationFlowKind? flowKind) =>
        clientSegmentId is not null || flowKind is not null;

    /// <summary>Throws <see cref="ArgumentException"/> with a CLI-appropriate message if the given
    /// combination of segment/flow-kind/key is neither a complete engine key nor a free-form key.</summary>
    public static void Validate(Guid? clientSegmentId, SimulationFlowKind? flowKind, string? key)
    {
        if (IsEngineParameter(clientSegmentId, flowKind))
        {
            if (clientSegmentId is null || flowKind is null)
                throw new ArgumentException("An engine parameter needs both --segment-id and --flow-kind.");
            return;
        }

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException(
                "Supply --segment-id + --flow-kind (an engine parameter), or --key (a free-form one).");
    }

    /// <summary>Formats the human-readable "what this parameter is keyed on" string the CLI prints
    /// after a successful set-param, purely from the inputs given (no server round trip needed).</summary>
    public static string DescribeTarget(Guid? clientSegmentId, SimulationFlowKind? flowKind, Guid? moduleProductId, string? key)
    {
        if (!IsEngineParameter(clientSegmentId, flowKind))
            return $"key '{key}'";

        var moduleSuffix = moduleProductId is null ? "" : $", module {moduleProductId}";
        return $"segment {clientSegmentId}, flow {flowKind}{moduleSuffix}";
    }
}
