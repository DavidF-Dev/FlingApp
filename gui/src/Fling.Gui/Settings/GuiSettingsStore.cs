using System.IO;
using System.Text.Json;

namespace Fling.Gui.Settings;

/// <summary>
/// Reads and writes the tray app's preferences.
/// </summary>
/// <remarks>
/// Deliberately more forgiving than the shared config store: a missing or damaged file
/// falls back to defaults rather than throwing. Losing a preference is an inconvenience,
/// and refusing to start over one would be worse.
/// </remarks>
public sealed class GuiSettingsStore
{
    private readonly string _filePath;

    public GuiSettingsStore(string filePath) => _filePath = filePath;

    public GuiSettingsStore() : this(GetDefaultPath())
    {
    }

    public GuiSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new GuiSettings();

            return JsonSerializer.Deserialize(File.ReadAllText(_filePath), GuiSettingsJsonContext.Default.GuiSettings)
                   ?? new GuiSettings();
        }
        catch (Exception)
        {
            return new GuiSettings();
        }
    }

    public void Save(GuiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var json = JsonSerializer.Serialize(settings, GuiSettingsJsonContext.Default.GuiSettings);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception)
        {
            // A preference that fails to persist is not worth interrupting the user for.
        }
    }

    public GuiSettings Update(Action<GuiSettings> mutate)
    {
        var settings = Load();
        mutate(settings);
        Save(settings);
        return settings;
    }

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Fling", "gui.json");
    }
}
