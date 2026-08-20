using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Forms;

namespace Fling.Gui.Windows;

/// <summary>
/// Positions a window on the display the user is currently working on.
/// </summary>
/// <remarks>
/// Centring on the primary display puts the window on the wrong monitor for anyone
/// working on a second one. The foreground window is the best available signal for
/// where the user is looking, and it has to be captured before this window is shown —
/// once it appears, it is the foreground window.
/// </remarks>
internal static partial class WindowPlacement
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    public static IntPtr CaptureActiveWindow() => GetForegroundWindow();

    /// <summary>
    /// Centres <paramref name="window"/> on the display holding
    /// <paramref name="activeWindow"/>, falling back to the primary display.
    /// </summary>
    /// <remarks>
    /// Works entirely in physical pixels through the window handle. WPF's own
    /// coordinates are awkward to reason about across monitors with different scaling,
    /// and the window's real size is not known until it has a handle.
    /// </remarks>
    public static void CenterOnActiveScreen(Window window, IntPtr activeWindow)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var screen = activeWindow != IntPtr.Zero
            ? Screen.FromHandle(activeWindow)
            : Screen.PrimaryScreen;

        if (screen is null || !GetWindowRect(handle, out var bounds))
            return;

        var area = screen.WorkingArea;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;

        var left = area.Left + (area.Width - width) / 2;
        var top = area.Top + (area.Height - height) / 2;

        SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
