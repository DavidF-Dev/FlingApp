using System.Diagnostics;
using System.IO;
using System.Windows;
using Fling.Config;
using Fling.Gui.Settings;
using Fling.Gui.ViewModels;
using Fling.Platform;

namespace Fling.Gui.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;
    private readonly IntPtr _activeWindow = WindowPlacement.CaptureActiveWindow();

    public SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();

        _model = model;
        DataContext = _model;

        // The CLI or Task Manager may have changed something since this window last ran.
        Activated += (_, _) => _model.Reload();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowPlacement.CenterOnActiveScreen(this, _activeWindow);
    }

    private void OnOpenConfigFolderClicked(object sender, RoutedEventArgs e) =>
        Reveal(_model.ConfigFolder);

    private void OnOpenLogClicked(object sender, RoutedEventArgs e) =>
        Reveal(_model.LogPath);

    /// <summary>
    /// Hands a path to the shell rather than launching a viewer, so the user's own
    /// association decides what opens.
    /// </summary>
    private static void Reveal(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
