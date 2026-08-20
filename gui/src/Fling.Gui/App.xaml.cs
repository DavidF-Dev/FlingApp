using System.Windows;
using System.Windows.Threading;
using Fling.Config;
using Fling.Content;
using Fling.Gui.Settings;
using Fling.Gui.Tray;
using Fling.Gui.ViewModels;
using Fling.Gui.Windows;
using Fling.Net;
using Fling.Operations;
using Fling.Platform;

namespace Fling.Gui;

public partial class App : Application
{
    /// <summary>
    /// Suppresses the window that a launch normally opens. Used by the sign-in entry,
    /// where the point is to sit in the tray rather than interrupt.
    /// </summary>
    public const string StartMinimizedArgument = "--minimized";

    private readonly ConfigStore _config = new();
    private readonly GuiSettingsStore _settings = new();

    private SingleInstance? _instance;
    private TrayIconHost? _tray;
    private SendNotifier? _notifier;
    private WindowManager? _windows;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startMinimized = e.Args.Any(a =>
            a.Equals(StartMinimizedArgument, StringComparison.OrdinalIgnoreCase));

        _instance = new SingleInstance();
        if (!_instance.TryAcquire())
        {
            // A sign-in launch racing an app that is already running has nothing to say;
            // any other launch means the user wants the window.
            if (!startMinimized)
                _instance.SignalExisting();

            Shutdown();
            return;
        }

        RepairStartupEntry();

        _windows = new WindowManager();
        _tray = new TrayIconHost(new TrayMenuActions(
            OpenFling: OpenFling,
            OpenDeviceManager: OpenDeviceManager,
            OpenSettings: OpenSettings,
            Quit: Shutdown));

        _notifier = new SendNotifier(_tray, _settings);

        // A window may have paired or removed a device, which the tooltip reports.
        _windows.WindowClosed += RefreshTooltip;
        RefreshTooltip();

        _instance.ListenForActivation(() => Dispatcher.Invoke(OpenFling));

        // Launching the app means "I want to send something"; only the sign-in entry
        // wants it to start out of the way.
        if (!startMinimized)
            OpenFling();
    }

    /// <summary>
    /// Opens the send window, or the device manager when there is nothing to send to —
    /// a window that cannot do anything is worse than the one that explains why. On a
    /// first run this makes launching the app land on pairing.
    /// </summary>
    private void OpenFling()
    {
        if (_config.Load().Devices.Count == 0)
        {
            OpenDeviceManager();
            return;
        }

        _windows!.Show(() => new FlingWindow(new FlingViewModel(
            _config,
            _settings,
            new WindowsClipboardReader(),
            new GdiImageEncoder(),
            new SendOperation(_config),
            _notifier)));
    }

    private void OpenDeviceManager() =>
        _windows!.Show(() => new DeviceManagerWindow(new DeviceManagerViewModel(
            _config,
            new ReachabilityProbe(_config),
            new UdpDiscovery(),
            new PairOperation(_config))));

    private void OpenSettings() =>
        _windows!.Show(() => new SettingsWindow(new SettingsViewModel(
            _config,
            _settings,
            new StartupRegistration(StartMinimizedArgument),
            new ExplorerSendToIntegration(SendToIntegration.ResolveExePath()))));

    private void RefreshTooltip()
    {
        var count = 0;
        try
        {
            count = _config.Load().Devices.Count;
        }
        catch (Exception)
        {
            // A damaged config is reported where the user can act on it, not in a tooltip.
        }

        _tray?.SetTooltip(count switch
        {
            0 => "Fling — no devices paired",
            1 => "Fling — 1 device paired",
            _ => $"Fling — {count} devices paired",
        });
    }

    /// <summary>
    /// Repoints a sign-in entry left behind by a copy that has since been moved. Does
    /// nothing when the user never asked to start at sign-in.
    /// </summary>
    private static void RepairStartupEntry()
    {
        try
        {
            new StartupRegistration(StartMinimizedArgument).HealIfMoved();
        }
        catch (Exception)
        {
            // The registry being unavailable is no reason to refuse to start.
        }
    }

    /// <summary>
    /// A tray app has no console, so an unhandled exception would otherwise end the
    /// process with nothing shown and the icon left behind.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Fling hit an unexpected error and will close.\n\n{e.Exception.Message}",
            "Fling",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _windows?.CloseAll();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
