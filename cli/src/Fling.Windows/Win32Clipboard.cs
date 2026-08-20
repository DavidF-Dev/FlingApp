using System.Runtime.InteropServices;

namespace Fling.Content;

/// <summary>
/// Supplies clipboard data by format identifier.
/// </summary>
internal interface IClipboardSource
{
    bool IsFormatAvailable(uint format);
    byte[]? GetBytes(uint format);
    uint RegisterFormat(string name);
}

/// <summary>
/// Reads the live Windows clipboard, holding it open for the duration of a read.
/// </summary>
internal sealed class Win32ClipboardSource : IClipboardSource
{
    public bool IsFormatAvailable(uint format) => Win32Clipboard.IsFormatAvailable(format);

    public byte[]? GetBytes(uint format) => Win32Clipboard.GetBytes(format);

    public uint RegisterFormat(string name) => Win32Clipboard.RegisterFormat(name);
}

/// <summary>
/// Minimal wrapper over the Win32 clipboard API. Reads raw bytes for a given format
/// without depending on a UI framework.
/// </summary>
internal static partial class Win32Clipboard
{
    public const uint CF_DIB = 8;
    public const uint CF_UNICODETEXT = 13;

    private const int OpenAttempts = 10;
    private const int OpenRetryDelayMs = 10;

    /// <summary>
    /// Opens the clipboard, invokes <paramref name="read"/>, and closes it. Returns the
    /// default value if the clipboard could not be opened.
    /// </summary>
    public static T? WithClipboard<T>(Func<T?> read)
    {
        if (!TryOpen())
            return default;

        try
        {
            return read();
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool IsFormatAvailable(uint format) => IsClipboardFormatAvailable(format);

    public static uint RegisterFormat(string name) => RegisterClipboardFormat(name);

    /// <summary>
    /// Copies the clipboard's data for a format into a managed array. The handle stays
    /// owned by the clipboard and must not be freed.
    /// </summary>
    public static byte[]? GetBytes(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero)
            return null;

        var size = (int)GlobalSize(handle);
        if (size <= 0)
            return null;

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new byte[size];
            Marshal.Copy(pointer, buffer, 0, size);
            return buffer;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// Another process may hold the clipboard open briefly; a short retry avoids failing
    /// a paste that would succeed a few milliseconds later.
    /// </summary>
    private static bool TryOpen()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
                return true;

            Thread.Sleep(OpenRetryDelayMs);
        }

        return false;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string lpszFormat);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint GlobalSize(IntPtr hMem);
}
