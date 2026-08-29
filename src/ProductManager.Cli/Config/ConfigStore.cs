using System.Text.Json;

namespace ProductManager.Cli.Config;

/// <summary>Reads and writes <see cref="CliConfig"/> at <c>~/.productmanager/config.json</c>.</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".productmanager");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
        }
        catch (JsonException)
        {
            // A corrupt config file should not crash the CLI — treat it as absent.
            return new CliConfig();
        }
    }

    public static void Save(CliConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}
