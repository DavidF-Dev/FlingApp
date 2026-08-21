using Fling.Config;
using Fling.Gui.Settings;
using Fling.Gui.ViewModels;
using Fling.Platform;

namespace Fling.Gui.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigStore _store;
    private readonly GuiSettingsStore _settingsStore;
    private readonly FakeStartup _startup = new();
    private readonly FakeShell _shell = new();

    public SettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-gui-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new ConfigStore(Path.Combine(_tempDir, "config.json"));
        _settingsStore = new GuiSettingsStore(Path.Combine(_tempDir, "gui.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SettingsViewModel Build() => new(_store, _settingsStore, _startup, _shell);

    // --- Shared settings -------------------------------------------------------------

    [Fact]
    public void New_ReadsCurrentConfig()
    {
        _store.Update(c =>
        {
            c.MaxSizeMb = 25;
            c.Compress = false;
            c.HostName = "Workshop";
            c.Log = true;
        });

        var model = Build();

        Assert.Equal(25, model.MaxSizeMb);
        Assert.False(model.Compress);
        Assert.Equal("Workshop", model.HostName);
        Assert.True(model.Log);
    }

    [Fact]
    public void MaxSizeMb_IsWrittenThroughToTheSharedConfig()
    {
        var model = Build();

        model.MaxSizeMb = 40;

        Assert.Equal(40, _store.Load().MaxSizeMb);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MaxSizeMb_NotPositive_IsRejectedAndConfigUnchanged(int invalid)
    {
        _store.Update(c => c.MaxSizeMb = 10);
        var model = Build();

        model.MaxSizeMb = invalid;

        Assert.Equal(10, model.MaxSizeMb);
        Assert.Equal(10, _store.Load().MaxSizeMb);
        Assert.NotNull(model.ErrorMessage);
    }

    [Fact]
    public void MaxSizeMb_ValidAfterInvalid_ClearsTheError()
    {
        var model = Build();
        model.MaxSizeMb = 0;

        model.MaxSizeMb = 20;

        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public void Compress_IsWrittenThrough()
    {
        var model = Build();

        model.Compress = false;

        Assert.False(_store.Load().Compress);
    }

    [Fact]
    public void HostName_IsTrimmed()
    {
        var model = Build();

        model.HostName = "  Workshop  ";

        Assert.Equal("Workshop", _store.Load().HostName);
    }

    [Fact]
    public void HostName_Empty_MeansFallBackToTheMachineName()
    {
        var model = Build();

        model.HostName = "";

        Assert.Equal("", _store.Load().HostName);
        Assert.Equal(Environment.MachineName, model.HostNamePlaceholder);
    }

    [Fact]
    public void Log_TogglesTheSharedSetting()
    {
        var model = Build();

        model.Log = true;

        Assert.True(_store.Load().Log);
    }

    /// <summary>
    /// The CLI writes the same file, so the window must not show a stale value.
    /// </summary>
    [Fact]
    public void Reload_PicksUpAChangeMadeElsewhere()
    {
        var model = Build();
        _store.Update(c => c.MaxSizeMb = 33);

        model.Reload();

        Assert.Equal(33, model.MaxSizeMb);
    }

    [Fact]
    public void SharedSettings_AreReadableByTheConfigStore()
    {
        var model = Build();

        model.MaxSizeMb = 15;
        model.Compress = false;
        model.HostName = "Desk";
        model.Log = true;

        var config = _store.Load();
        Assert.Equal(15, config.MaxSizeMb);
        Assert.False(config.Compress);
        Assert.Equal("Desk", config.HostName);
        Assert.True(config.Log);
    }

    [Fact]
    public void SharedSettings_DoNotDisturbPairedDevices()
    {
        _store.Update(c => c.Devices.Add(new DeviceConfig { Name = "Pixel", Host = "10.0.0.1", ApiKey = "key" }));
        var model = Build();

        model.MaxSizeMb = 12;

        Assert.Equal("Pixel", _store.Load().Devices.Single().Name);
    }

    // --- App preferences -------------------------------------------------------------

    [Fact]
    public void Notifications_IsWrittenThrough()
    {
        var model = Build();

        model.Notifications = NotificationMode.Always;

        Assert.Equal(NotificationMode.Always, _settingsStore.Load().Notifications);
    }

    [Fact]
    public void Notifications_DefaultsToFailuresOnly()
    {
        Assert.Equal(NotificationMode.FailuresOnly, Build().Notifications);
    }

    [Fact]
    public void RememberLastDevice_IsWrittenThrough()
    {
        var model = Build();

        model.RememberLastDevice = false;

        Assert.False(_settingsStore.Load().RememberLastDevice);
    }

    [Fact]
    public void AppPreferences_DoNotTouchTheSharedConfig()
    {
        var before = File.Exists(Path.Combine(_tempDir, "config.json"));
        var model = Build();

        model.Notifications = NotificationMode.Never;

        Assert.True(File.Exists(Path.Combine(_tempDir, "gui.json")));
        Assert.Equal(before, File.Exists(Path.Combine(_tempDir, "config.json")));
    }

    // --- Startup and shell integration -----------------------------------------------

    [Fact]
    public void RunAtStartup_TogglesRegistration()
    {
        var model = Build();

        model.RunAtStartup = true;
        Assert.True(_startup.Enabled);
        Assert.True(model.RunAtStartup);

        model.RunAtStartup = false;
        Assert.False(_startup.Enabled);
        Assert.False(model.RunAtStartup);
    }

    /// <summary>
    /// Task Manager can switch this off behind the app's back, so the property reads the
    /// live state rather than what was last set here.
    /// </summary>
    [Fact]
    public void RunAtStartup_ChangedExternally_IsReflected()
    {
        var model = Build();
        model.RunAtStartup = true;

        _startup.Enabled = false;

        Assert.False(model.RunAtStartup);
    }

    [Fact]
    public void SendToInstalled_TogglesShellIntegration()
    {
        var model = Build();

        model.SendToInstalled = true;
        Assert.True(_shell.Installed);

        model.SendToInstalled = false;
        Assert.False(_shell.Installed);
    }

    [Fact]
    public void SendToInstalled_FailureIsReportedNotThrown()
    {
        _shell.ThrowOnInstall = true;
        var model = Build();

        model.SendToInstalled = true;

        Assert.False(model.SendToInstalled);
        Assert.Contains("Send to menu", model.ErrorMessage);
    }

    // --- Robustness ------------------------------------------------------------------

    [Fact]
    public void Reload_AfterGuiSettingsFileDeleted_FallsBackToDefaults()
    {
        var model = Build();
        model.Notifications = NotificationMode.Always;

        File.Delete(Path.Combine(_tempDir, "gui.json"));
        model.Reload();

        Assert.Equal(NotificationMode.FailuresOnly, model.Notifications);
    }

    [Fact]
    public void Reload_AfterGuiSettingsFileCorrupted_FallsBackToDefaults()
    {
        var model = Build();
        File.WriteAllText(Path.Combine(_tempDir, "gui.json"), "{ this is not json");

        model.Reload();

        Assert.Equal(NotificationMode.FailuresOnly, model.Notifications);
    }

    [Fact]
    public void CanOpenLog_IsFalseWhenLoggingIsOff()
    {
        var model = Build();

        model.Log = false;

        Assert.False(model.CanOpenLog);
    }

    // --- Fakes -----------------------------------------------------------------------

    private sealed class FakeStartup : IStartupRegistration
    {
        public bool Enabled { get; set; }
        public bool IsEnabled() => Enabled;
        public void Enable() => Enabled = true;
        public void Disable() => Enabled = false;
        public bool RepairIfStale() => false;
    }

    private sealed class FakeShell : IShellIntegration
    {
        public bool Installed { get; private set; }
        public bool ThrowOnInstall { get; set; }

        public bool IsInstalled() => Installed;

        public void Install()
        {
            if (ThrowOnInstall)
                throw new InvalidOperationException("no Send to folder");

            Installed = true;
        }

        public void Uninstall() => Installed = false;
    }
}
