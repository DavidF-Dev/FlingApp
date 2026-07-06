using System.Text.Json;

namespace Fling.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public ConfigStore(string filePath)
    {
        _filePath = filePath;
    }

    public ConfigStore()
        : this(GetDefaultPath())
    {
    }

    public FlingConfig Load()
    {
        if (!File.Exists(_filePath))
            return new FlingConfig();

        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (IOException ex)
        {
            throw new ConfigException($"Could not read config file at {_filePath}: {ex.Message}", ex);
        }

        try
        {
            return JsonSerializer.Deserialize<FlingConfig>(json, JsonOptions)
                   ?? new FlingConfig();
        }
        catch (JsonException ex)
        {
            throw new ConfigException(
                $"Config file at {_filePath} contains invalid JSON. Fix or delete the file to continue.\n{ex.Message}",
                ex);
        }
    }

    public void Save(FlingConfig config)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Fling", "config.json");
    }
}
