using System.CommandLine;
using Fling.Commands;
using Fling.Config;

namespace Fling.Tests;

public sealed class StatusCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly ConfigStore _store;

    public StatusCommandTests()
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
        rootCommand.Subcommands.Add(StatusCommand.Create(_store));
        return rootCommand.Parse(args).Invoke();
    }

    [Fact]
    public void NoDevices_ReturnsError()
    {
        _store.Save(new FlingConfig());

        var exitCode = Invoke("status");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void UnknownDevice_ReturnsError()
    {
        _store.Save(new FlingConfig
        {
            Devices = [new DeviceConfig { Name = "Phone A", Host = "10.0.0.1", ApiKey = "k" }],
        });

        var exitCode = Invoke("status", "--device", "Nonexistent");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void UnreachableDevice_ReturnsNonZeroExit()
    {
        _store.Save(new FlingConfig
        {
            Devices = [new DeviceConfig { Name = "Offline Phone", Host = "192.0.2.1", Port = 7291, ApiKey = "k" }],
        });

        var exitCode = Invoke("status");

        Assert.Equal(2, exitCode);
    }
}
