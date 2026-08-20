using System.IO;
using System.Reflection;
using Fling.Config;
using Fling.Gui.Settings;
using Fling.Platform;

namespace Fling.Gui.ViewModels;

/// <summary>
/// Drives the settings window. Changes apply as they are made — there is no Save button
/// to forget to press — so each setter writes through to its store.
/// </summary>
/// <remarks>
/// Settings are deliberately split by where they live. The first group is shared with
/// `fling.exe` and lives in the config file; the rest belong to the tray app alone.
/// </remarks>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ConfigStore _store;
    private readonly GuiSettingsStore _settingsStore;
    private readonly IStartupRegistration _startup;
    private readonly IShellIntegration _shell;

    private int _maxSizeMb;
    private bool _compress;
    private string _hostName = "";
    private bool _log;
    private NotificationMode _notifications;
    private bool _rememberLastDevice;
    private string? _errorMessage;

    public SettingsViewModel(
        ConfigStore store,
        GuiSettingsStore settingsStore,
        IStartupRegistration startup,
        IShellIntegration shell)
    {
        _store = store;
        _settingsStore = settingsStore;
        _startup = startup;
        _shell = shell;

        Reload();
    }

    // --- Shared with the CLI ---------------------------------------------------------

    public int MaxSizeMb
    {
        get => _maxSizeMb;
        set
        {
            if (value <= 0)
            {
                ErrorMessage = "Maximum size must be greater than 0 MB.";
                Raise();
                return;
            }

            if (!Set(ref _maxSizeMb, value))
                return;

            ErrorMessage = null;
            _store.Update(c => c.MaxSizeMb = value);
        }
    }

    public bool Compress
    {
        get => _compress;
        set
        {
            if (Set(ref _compress, value))
                _store.Update(c => c.Compress = value);
        }
    }

    /// <summary>
    /// The name devices show for this PC. Empty means fall back to the machine name.
    /// </summary>
    public string HostName
    {
        get => _hostName;
        set
        {
            var trimmed = value.Trim();
            if (Set(ref _hostName, trimmed))
                _store.Update(c => c.HostName = trimmed);
        }
    }

    public string HostNamePlaceholder => Environment.MachineName;

    public bool Log
    {
        get => _log;
        set
        {
            if (!Set(ref _log, value))
                return;

            _store.Update(c => c.Log = value);
            Raise(nameof(CanOpenLog));
        }
    }

    // --- Tray app only ---------------------------------------------------------------

    public NotificationMode Notifications
    {
        get => _notifications;
        set
        {
            if (Set(ref _notifications, value))
                _settingsStore.Update(s => s.Notifications = value);
        }
    }

    public bool RememberLastDevice
    {
        get => _rememberLastDevice;
        set
        {
            if (Set(ref _rememberLastDevice, value))
                _settingsStore.Update(s => s.RememberLastDevice = value);
        }
    }

    /// <summary>
    /// Read straight from the registry rather than cached, because Task Manager's Startup
    /// tab can turn this off behind the app's back.
    /// </summary>
    public bool RunAtStartup
    {
        get => _startup.IsEnabled();
        set
        {
            if (value)
                _startup.Enable();
            else
                _startup.Disable();

            Raise();
        }
    }

    public bool SendToInstalled
    {
        get => _shell.IsInstalled();
        set
        {
            try
            {
                if (value)
                    _shell.Install();
                else
                    _shell.Uninstall();

                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not update the Send to menu: {ex.Message}";
            }

            Raise();
        }
    }

    // --- About -----------------------------------------------------------------------

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    public string ConfigFolder => Path.GetDirectoryName(ConfigStore.DefaultPath)!;

    public string LogPath => Path.Combine(ConfigFolder, "fling.log");

    public bool CanOpenLog => Log && File.Exists(LogPath);

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    /// <summary>
    /// Re-reads everything from disk and the registry. Called when the window opens, so
    /// a change made by the CLI or by Task Manager meanwhile is reflected.
    /// </summary>
    public void Reload()
    {
        var config = _store.Load();
        _maxSizeMb = config.MaxSizeMb;
        _compress = config.Compress;
        _hostName = config.HostName;
        _log = config.Log;

        var settings = _settingsStore.Load();
        _notifications = settings.Notifications;
        _rememberLastDevice = settings.RememberLastDevice;

        Raise(nameof(MaxSizeMb));
        Raise(nameof(Compress));
        Raise(nameof(HostName));
        Raise(nameof(Log));
        Raise(nameof(Notifications));
        Raise(nameof(RememberLastDevice));
        Raise(nameof(RunAtStartup));
        Raise(nameof(SendToInstalled));
        Raise(nameof(CanOpenLog));
    }
}
