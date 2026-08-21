using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;

namespace Fling.Gui.Tray;

/// <summary>
/// Owns the notification-area icon and its menu.
/// </summary>
/// <remarks>
/// Must be constructed on the UI thread: the icon relies on that thread's message pump
/// to deliver click and menu events.
/// </remarks>
public sealed partial class TrayIconHost : IDisposable
{
    private static readonly TimeSpan SuccessFlashDuration = TimeSpan.FromSeconds(1.5);

    private readonly NotifyIcon _icon;
    private readonly Icon _baseIcon;
    private readonly Icon? _successIcon;
    private readonly IntPtr _successIconHandle;
    private readonly DispatcherTimer _flashTimer;

    public TrayIconHost(TrayMenuActions actions)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("&Fling…", null, (_, _) => actions.OpenFling());
        menu.Items.Add("&Device manager", null, (_, _) => actions.OpenDeviceManager());
        menu.Items.Add("&Settings", null, (_, _) => actions.OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Quit", null, (_, _) => actions.Quit());

        _baseIcon = LoadIcon();
        _successIcon = TryCreateSuccessIcon(_baseIcon, out _successIconHandle);

        _icon = new NotifyIcon
        {
            Icon = _baseIcon,
            Text = "Fling",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => actions.OpenFling();

        _flashTimer = new DispatcherTimer { Interval = SuccessFlashDuration };
        _flashTimer.Tick += (_, _) => RestoreIcon();
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon)
    {
        _icon.BalloonTipTitle = title.Length <= 63 ? title : title[..63];
        _icon.BalloonTipText = message.Length <= 255 ? message : message[..255];
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(0);
    }

    /// <summary>
    /// Marks the icon briefly, so a send that closed its window still confirms itself
    /// without interrupting with a notification.
    /// </summary>
    public void FlashSuccess()
    {
        if (_successIcon is null)
            return;

        _icon.Icon = _successIcon;
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    /// <summary>
    /// An icon left undisposed lingers in the notification area until the user hovers
    /// over it, so this must run before the process exits.
    /// </summary>
    public void Dispose()
    {
        _flashTimer.Stop();
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();

        _successIcon?.Dispose();
        if (_successIconHandle != IntPtr.Zero)
            DestroyIcon(_successIconHandle);

        _baseIcon.Dispose();
    }

    private void RestoreIcon()
    {
        _flashTimer.Stop();
        _icon.Icon = _baseIcon;
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
                return (Icon)SystemIcons.Application.Clone();

            // Picks the frame matching the notification area rather than scaling a large one.
            return new Icon(embedded, SystemInformation.SmallIconSize);
        }
        catch (Exception)
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    /// <summary>
    /// Composites a small marker onto the app icon. The handle is returned separately
    /// because an icon built this way owns an unmanaged handle that Dispose does not
    /// release.
    /// </summary>
    private static Icon? TryCreateSuccessIcon(Icon baseIcon, out IntPtr handle)
    {
        handle = IntPtr.Zero;

        try
        {
            using var bitmap = baseIcon.ToBitmap();
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var diameter = Math.Max(6, bitmap.Width / 2);
            var marker = new Rectangle(
                bitmap.Width - diameter,
                bitmap.Height - diameter,
                diameter - 1,
                diameter - 1);

            using var fill = new SolidBrush(Color.FromArgb(0x2E, 0x7D, 0x32));
            using var edge = new Pen(Color.White, Math.Max(1f, diameter / 8f));
            graphics.FillEllipse(fill, marker);
            graphics.DrawEllipse(edge, marker);

            handle = bitmap.GetHicon();
            return Icon.FromHandle(handle);
        }
        catch (Exception)
        {
            // Without a marker the tray simply does not flash; nothing else depends on it.
            return null;
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);
}

/// <summary>
/// What the tray menu can do. Keeps the icon free of any knowledge of windows.
/// </summary>
public sealed record TrayMenuActions(
    Action OpenFling,
    Action OpenDeviceManager,
    Action OpenSettings,
    Action Quit);
