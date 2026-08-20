using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fling.Config;

public sealed class FlingConfig
{
    public List<DeviceConfig> Devices { get; set; } = [];
    public int MaxSizeMb { get; set; } = 10;
    public bool Compress { get; set; } = true;
    public string HostName { get; set; } = "";
    public bool Log { get; set; }

    /// <summary>
    /// Properties present in the file but unknown to this build. Captured on load and
    /// written back on save so an older binary cannot silently discard settings written
    /// by a newer one.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
