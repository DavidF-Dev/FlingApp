using System.Text.Json;
using Fling.Config;

namespace Fling.Tests;

public sealed class ConfigStoreConcurrencyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly ConfigStore _store;

    public ConfigStoreConcurrencyTests()
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

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        _store.Save(new FlingConfig { MaxSizeMb = 25 });

        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public void Save_FailureBeforeSwap_LeavesPreviousFileIntact()
    {
        _store.Save(new FlingConfig { MaxSizeMb = 11 });
        var original = File.ReadAllText(_configPath);

        // Holding the destination open blocks the swap, standing in for a crash between
        // writing the replacement and putting it in place.
        using (var hold = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.NotNull(Record.Exception(() => _store.Save(new FlingConfig { MaxSizeMb = 99 })));
        }

        Assert.Equal(original, File.ReadAllText(_configPath));
        Assert.Equal(11, _store.Load().MaxSizeMb);
    }

    [Fact]
    public void Save_ConcurrentWrites_NeverProduceUnreadableJson()
    {
        _store.Save(new FlingConfig());

        var failures = 0;
        Parallel.For(0, 40, i =>
        {
            var store = new ConfigStore(_configPath);
            try
            {
                store.Save(new FlingConfig { MaxSizeMb = 10 + (i % 20) });
                store.Load();
            }
            catch (ConfigException)
            {
                Interlocked.Increment(ref failures);
            }
        });

        Assert.Equal(0, failures);
    }

    [Fact]
    public void Update_ConcurrentIncrements_AreNotLost()
    {
        _store.Save(new FlingConfig { Devices = [] });

        Parallel.For(0, 20, i =>
        {
            var store = new ConfigStore(_configPath);
            store.Update(c => c.Devices.Add(new DeviceConfig { Name = $"device-{i}" }));
        });

        Assert.Equal(20, _store.Load().Devices.Count);
    }

    [Fact]
    public void Update_ReturnsTheSavedConfig()
    {
        var result = _store.Update(c => c.MaxSizeMb = 42);

        Assert.Equal(42, result.MaxSizeMb);
        Assert.Equal(42, _store.Load().MaxSizeMb);
    }

    [Fact]
    public void Load_UnknownProperty_SurvivesRoundTrip()
    {
        File.WriteAllText(_configPath, """
            {
              "devices": [],
              "maxSizeMb": 10,
              "compress": true,
              "hostName": "",
              "log": false,
              "futureSetting": { "nested": 7 }
            }
            """);

        _store.Update(c => c.MaxSizeMb = 20);

        using var document = JsonDocument.Parse(File.ReadAllText(_configPath));
        var future = document.RootElement.GetProperty("futureSetting");

        Assert.Equal(7, future.GetProperty("nested").GetInt32());
        Assert.Equal(20, document.RootElement.GetProperty("maxSizeMb").GetInt32());
    }

    [Fact]
    public void Load_UnknownScalarProperty_SurvivesRoundTrip()
    {
        File.WriteAllText(_configPath, """{"maxSizeMb": 10, "trayHotkey": "Ctrl+Alt+V"}""");

        _store.Save(_store.Load());

        using var document = JsonDocument.Parse(File.ReadAllText(_configPath));
        Assert.Equal("Ctrl+Alt+V", document.RootElement.GetProperty("trayHotkey").GetString());
    }
}
