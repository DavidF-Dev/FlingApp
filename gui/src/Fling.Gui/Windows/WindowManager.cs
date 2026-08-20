using System.Windows;

namespace Fling.Gui.Windows;

/// <summary>
/// Keeps at most one live window per type, so re-invoking a tray menu item brings the
/// existing window forward rather than opening a duplicate.
/// </summary>
public sealed class WindowManager
{
    private readonly Dictionary<Type, Window> _open = [];

    /// <summary>
    /// Raised whenever a window closes, so the caller can refresh anything the window
    /// might have changed.
    /// </summary>
    public event Action? WindowClosed;

    /// <summary>
    /// Shows the window of this type, creating it with <paramref name="create"/> only if
    /// one is not already open. The factory keeps window construction — and the
    /// dependencies each window needs — with the caller rather than here.
    /// </summary>
    public void Show<TWindow>(Func<TWindow> create) where TWindow : Window
    {
        if (_open.TryGetValue(typeof(TWindow), out var existing))
        {
            Surface(existing);
            return;
        }

        var window = create();
        _open[typeof(TWindow)] = window;
        window.Closed += (_, _) =>
        {
            _open.Remove(typeof(TWindow));
            WindowClosed?.Invoke();
        };

        window.Show();
        Surface(window);
    }

    public void CloseAll()
    {
        foreach (var window in _open.Values.ToList())
            window.Close();
    }

    /// <summary>
    /// A window minimised or behind other applications will not come forward from Show
    /// or Activate alone; the state has to be restored first.
    /// </summary>
    private static void Surface(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}
