using System.CommandLine;
using Fling.Commands;
using Fling.Config;

namespace Fling.Tests;

public sealed class ConfigCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly ConfigStore _store;

    public ConfigCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _store = new ConfigStore(_configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private int Invoke(params string[] args)
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(ConfigCommand.Create(_store));
        return rootCommand.Parse(args).Invoke();
    }

    [Fact]
    public void Set_MaxSize_UpdatesConfig()
    {
        Invoke("config", "set", "--max-size", "25");

        var config = _store.Load();
        Assert.Equal(25, config.MaxSizeMb);
    }

    [Fact]
    public void Set_Compress_UpdatesConfig()
    {
        Invoke("config", "set", "--compress", "false");

        var config = _store.Load();
        Assert.False(config.Compress);
    }

    [Fact]
    public void Set_BothOptions_UpdatesConfig()
    {
        Invoke("config", "set", "--max-size", "50", "--compress", "false");

        var config = _store.Load();
        Assert.Equal(50, config.MaxSizeMb);
        Assert.False(config.Compress);
    }

    [Fact]
    public void Set_NoOptions_ReturnsError()
    {
        var exitCode = Invoke("config", "set");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void Set_InvalidMaxSize_ReturnsError()
    {
        var exitCode = Invoke("config", "set", "--max-size", "0");

        Assert.NotEqual(0, exitCode);
        var config = _store.Load();
        Assert.Equal(10, config.MaxSizeMb);
    }

    [Fact]
    public void Set_NegativeMaxSize_ReturnsError()
    {
        var exitCode = Invoke("config", "set", "--max-size", "-5");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void Default_SetsDeviceAsDefault()
    {
        _store.Save(new FlingConfig
        {
            Devices =
            [
                new DeviceConfig { Name = "Phone A", Host = "10.0.0.1", ApiKey = "k1", Default = true },
                new DeviceConfig { Name = "Phone B", Host = "10.0.0.2", ApiKey = "k2" },
            ],
        });

        Invoke("config", "default", "Phone B");

        var config = _store.Load();
        Assert.False(config.Devices[0].Default);
        Assert.True(config.Devices[1].Default);
    }

    [Fact]
    public void Default_UnknownDevice_ReturnsError()
    {
        _store.Save(new FlingConfig());

        var exitCode = Invoke("config", "default", "Nonexistent");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void Remove_RemovesDevice()
    {
        _store.Save(new FlingConfig
        {
            Devices =
            [
                new DeviceConfig { Name = "Phone A", Host = "10.0.0.1", ApiKey = "k1" },
                new DeviceConfig { Name = "Phone B", Host = "10.0.0.2", ApiKey = "k2" },
            ],
        });

        Invoke("config", "remove", "Phone A");

        var config = _store.Load();
        Assert.Single(config.Devices);
        Assert.Equal("Phone B", config.Devices[0].Name);
    }

    [Fact]
    public void Remove_UnknownDevice_ReturnsError()
    {
        _store.Save(new FlingConfig());

        var exitCode = Invoke("config", "remove", "Nonexistent");

        Assert.NotEqual(0, exitCode);
    }
}
