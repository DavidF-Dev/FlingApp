using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fling.Gui.Settings;

public enum NotificationMode
{
    Always,
    FailuresOnly,
    Never,
}

/// <summary>
/// Tray app preferences, stored separately from the shared config so that frequent
/// writes never touch the file holding the device API keys.
/// </summary>
public sealed class GuiSettings
{
    public NotificationMode Notifications { get; set; } = NotificationMode.FailuresOnly;
    public bool RememberLastDevice { get; set; } = true;
    public string LastDevice { get; set; } = "";
    public bool FirstRunComplete { get; set; }

    /// <summary>
    /// Preferences written by a newer build, preserved so an older one cannot drop them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(GuiSettings))]
internal sealed partial class GuiSettingsJsonContext : JsonSerializerContext;
