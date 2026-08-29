using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProductManager.Api.Simulation;

/// <summary>
/// The single serializer used for everything a simulation run stores. Run snapshots and results are
/// persisted JSON that must round-trip exactly for the reproducibility guarantee to hold, so the
/// options live in one place rather than being rebuilt (and drifting) at each call site. Matches the
/// API wire format: camelCase properties, enums by declared member name.
/// </summary>
public static class SimulationJson
{
    public static readonly JsonSerializerOptions Options = Create();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        // Freezing with the default reflection resolver: shared static options must not be mutable
        // once anything has serialized through them.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
