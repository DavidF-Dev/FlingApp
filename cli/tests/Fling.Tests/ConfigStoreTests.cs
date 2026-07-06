using System.Text.Json;
using Fling.Config;

namespace Fling.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new ConfigStore(_configPath);

        var config = store.Load();

        Assert.Empty(config.Devices);
        Assert.Equal(10, config.MaxSizeMb);
        Assert.True(config.Compress);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var store = new ConfigStore(_configPath);
        var original = new FlingConfig
        {
            Devices =
            [
                new DeviceConfig
                {
                    Name = "Pixel 8",
                    Host = "192.168.1.50",
                    Port = 7291,
                    ApiKey = "test-key-abc123",
                    Default = true,
                },
                new DeviceConfig
                {
                    Name = "Galaxy S24",
                    Host = "192.168.1.51",
                    Port = 8000,
                    ApiKey = "test-key-def456",
                    Default = false,
                },
            ],
            MaxSizeMb = 25,
            Compress = false,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.MaxSizeMb, loaded.MaxSizeMb);
        Assert.Equal(original.Compress, loaded.Compress);
        Assert.Equal(original.Devices.Count, loaded.Devices.Count);

        for (var i = 0; i < original.Devices.Count; i++)
        {
            Assert.Equal(original.Devices[i].Name, loaded.Devices[i].Name);
            Assert.Equal(original.Devices[i].Host, loaded.Devices[i].Host);
            Assert.Equal(original.Devices[i].Port, loaded.Devices[i].Port);
            Assert.Equal(original.Devices[i].ApiKey, loaded.Devices[i].ApiKey);
            Assert.Equal(original.Devices[i].Default, loaded.Devices[i].Default);
        }
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "dir", "config.json");
        var store = new ConfigStore(nestedPath);

        store.Save(new FlingConfig());

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void Load_CorruptJson_ThrowsConfigException()
    {
        File.WriteAllText(_configPath, "{ not valid json!!!");
        var store = new ConfigStore(_configPath);

        var ex = Assert.Throws<ConfigException>(() => store.Load());
        Assert.Contains(_configPath, ex.Message);
        Assert.Contains("invalid JSON", ex.Message);
    }

    [Fact]
    public void Save_UsesCamelCasePropertyNames()
    {
        var store = new ConfigStore(_configPath);
        var config = new FlingConfig
        {
            Devices =
            [
                new DeviceConfig
                {
                    Name = "Test",
                    Host = "10.0.0.1",
                    ApiKey = "key123",
                    Default = true,
                },
            ],
            MaxSizeMb = 5,
        };

        store.Save(config);
        var json = File.ReadAllText(_configPath);

        Assert.Contains("\"maxSizeMb\"", json);
        Assert.Contains("\"apiKey\"", json);
        Assert.DoesNotContain("\"MaxSizeMb\"", json);
        Assert.DoesNotContain("\"ApiKey\"", json);
    }
}
