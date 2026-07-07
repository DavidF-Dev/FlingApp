namespace Fling.Config;

public sealed class FlingConfig
{
    public List<DeviceConfig> Devices { get; set; } = [];
    public int MaxSizeMb { get; set; } = 10;
    public bool Compress { get; set; } = true;
    public string HostName { get; set; } = "";
    public bool Log { get; set; }
}
