namespace Fling.Config;

public sealed class DeviceConfig
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 7291;
    public string ApiKey { get; set; } = "";
    public bool Default { get; set; }
}
