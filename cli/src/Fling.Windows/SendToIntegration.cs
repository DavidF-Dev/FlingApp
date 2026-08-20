using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Fling.Platform;

/// <summary>
/// Whether Fling appears in Explorer's "Send to" menu.
/// </summary>
public interface IShellIntegration
{
    bool IsInstalled();
    void Install();
    void Uninstall();
}

/// <summary>
/// Installs and removes the "Send to" shortcut, pointing it at the CLI.
/// </summary>
/// <remarks>
/// Explorer passes the selected file as an argument, so the shortcut targets the
/// console executable rather than the tray app.
/// </remarks>
public sealed class ExplorerSendToIntegration(string targetPath) : IShellIntegration
{
    private const string Arguments = "send --all --file";

    public bool IsInstalled() => SendToIntegration.IsInstalled();

    public void Install()
    {
        var path = SendToIntegration.GetShortcutPath()
                   ?? throw new InvalidOperationException("Could not locate the Send to folder.");

        SendToIntegration.Install(path, targetPath, Arguments);
    }

    public void Uninstall()
    {
        var path = SendToIntegration.GetShortcutPath();
        if (path is not null && File.Exists(path))
            File.Delete(path);
    }
}

/// <summary>
/// Manages the Fling shortcut in the user's Windows "Send to" menu.
/// </summary>
public static partial class SendToIntegration
{
    private const string ShortcutName = "Fling.lnk";
    private const uint CLSCTX_INPROC_SERVER = 1;

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    public static string? GetShortcutPath()
    {
        var sendToDir = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        return string.IsNullOrEmpty(sendToDir) ? null : Path.Combine(sendToDir, ShortcutName);
    }

    public static bool IsInstalled()
    {
        var path = GetShortcutPath();
        return path is not null && File.Exists(path);
    }

    /// <summary>
    /// Writes a shell shortcut.
    /// </summary>
    /// <remarks>
    /// Uses IShellLink through a source-generated COM wrapper. The marshalling stubs are
    /// emitted at compile time, so trimming cannot discard the interface metadata that
    /// vtable dispatch depends on.
    /// </remarks>
    public static void Install(string shortcutPath, string targetPath, string arguments)
    {
        var hr = CoCreateInstance(in CLSID_ShellLink, IntPtr.Zero, CLSCTX_INPROC_SERVER, in IID_IShellLinkW, out var instance);
        if (hr < 0)
            throw new InvalidOperationException($"Could not create a shell link object (HRESULT 0x{hr:X8}).");

        var wrappers = new StrategyBasedComWrappers();
        var link = (IShellLinkW)wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.None);

        // The wrapper holds its own reference; this one is ours to drop.
        Marshal.Release(instance);

        try
        {
            link.SetPath(targetPath);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath)!);
            ((IPersistFile)link).Save(shortcutPath, fRemember: true);
        }
        finally
        {
            (link as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Prefers the GUI-subsystem variant sitting alongside the running executable, so a
    /// launch from Explorer does not flash a console window. Falls back to the current
    /// executable when that variant is absent, as it is during development.
    /// </summary>
    public static string ResolveExePath()
    {
        var self = Environment.ProcessPath!;
        var selfName = Path.GetFileNameWithoutExtension(self);
        var dir = Path.GetDirectoryName(self)!;

        if (selfName.Equals("flingw", StringComparison.OrdinalIgnoreCase))
            return self;

        var flingwPath = Path.Combine(dir, "flingw.exe");
        return File.Exists(flingwPath) ? flingwPath : self;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);
}

/// <summary>
/// Members are declared in vtable order. Every method up to the last one used must be
/// present, including those never called; buffer parameters on those are left as raw
/// pointers because their marshalling is irrelevant.
/// </summary>
[GeneratedComInterface]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription(IntPtr pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory(IntPtr pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments(IntPtr pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[GeneratedComInterface]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile(out IntPtr ppszFileName);
}
