using System.Windows;
using System.Windows.Threading;
using Fling.Config;
using Fling.Gui.Tray;
using Fling.Gui.Windows;

namespace Fling.Gui;

public partial class App : Application
{
    private SingleInstance? _instance;
    private TrayIconHost? _tray;
    private WindowManager? _windows;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new SingleInstance();
        if (!_instance.TryAcquire())
        {
            _instance.SignalExisting();
            Shutdown();
            return;
        }

        _windows = new WindowManager();
        _tray = new TrayIconHost(new TrayMenuActions(
            OpenFling: OpenFling,
            OpenDeviceManager: () => _windows.Show<DeviceManagerWindow>(),
            OpenSettings: () => _windows.Show<SettingsWindow>(),
            Quit: Shutdown));

        _instance.ListenForActivation(() => Dispatcher.Invoke(OpenFling));
    }

    /// <summary>
    /// Opens the send window, or the device manager when there is nothing to send to —
    /// a window that cannot do anything is worse than the one that explains why.
    /// </summary>
    private void OpenFling()
    {
        if (new ConfigStore().Load().Devices.Count == 0)
        {
            _windows!.Show<DeviceManagerWindow>();
            return;
        }

        _windows!.Show<FlingWindow>();
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
