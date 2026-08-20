using System.Drawing;
using System.Windows.Forms;

namespace Fling.Gui.Tray;

/// <summary>
/// Owns the notification-area icon and its menu.
/// </summary>
/// <remarks>
/// Must be constructed on the UI thread: the icon relies on that thread's message pump
/// to deliver click and menu events.
/// </remarks>
public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIconHost(TrayMenuActions actions)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("&Fling…", null, (_, _) => actions.OpenFling());
        menu.Items.Add("&Device manager", null, (_, _) => actions.OpenDeviceManager());
        menu.Items.Add("&Settings", null, (_, _) => actions.OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Quit", null, (_, _) => actions.Quit());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Fling",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => actions.OpenFling();
    }

    public void SetTooltip(string text)
    {
        // NotifyIcon truncates silently past 63 characters.
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    /// <summary>
    /// An icon left undisposed lingers in the notification area until the user hovers
    /// over it, so this must run before the process exits.
    /// </summary>
    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    /// <summary>
    /// Takes the icon from the executable's own Win32 resources rather than embedding a
    /// second copy, which also guarantees the tray and Explorer show the same image.
    /// </summary>
    private static Icon LoadIcon()
    {
        try
        {
            using var embedded = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            if (embedded is null)
                return SystemIcons.Application;

            // Picks the frame matching the notification area rather than scaling a large one.
            return new Icon(embedded, SystemInformation.SmallIconSize);
        }
        catch (Exception)
        {
            return SystemIcons.Application;
        }
    }
}

/// <summary>
/// What the tray menu can do. Keeps the icon free of any knowledge of windows.
/// </summary>
public sealed record TrayMenuActions(
    Action OpenFling,
    Action OpenDeviceManager,
    Action OpenSettings,
    Action Quit);
