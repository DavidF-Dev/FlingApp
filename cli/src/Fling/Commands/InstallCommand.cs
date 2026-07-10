using System.CommandLine;
using System.Runtime.InteropServices;

namespace Fling.Commands;

public static class InstallCommand
{
    public static Command Create()
    {
        var command = new Command("install", "Add Fling to the Windows 'Send to' menu");

        command.SetAction(_ =>
        {
            var sendToDir = GetSendToDirectory();
            if (sendToDir is null)
            {
                Console.Error.WriteLine("Could not locate the SendTo directory.");
                return 1;
            }

            var exePath = ResolveExePath();
            var shortcutPath = Path.Combine(sendToDir, "Fling.lnk");

            CreateShortcut(shortcutPath, exePath, "send --all --file");

            Console.WriteLine($"Installed: {shortcutPath}");
            Console.WriteLine($"Target:    {exePath}");
            Console.WriteLine("Right-click a file in Explorer and choose Send to > Fling.");
            return 0;
        });

        return command;
    }

    internal static string ResolveExePath()
    {
        var self = Environment.ProcessPath!;
        var selfName = Path.GetFileNameWithoutExtension(self);
        var dir = Path.GetDirectoryName(self)!;

        if (selfName.Equals("flingw", StringComparison.OrdinalIgnoreCase))
            return self;

        var flingwPath = Path.Combine(dir, "flingw.exe");
        return File.Exists(flingwPath) ? flingwPath : self;
    }

    private static string? GetSendToDirectory()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string arguments)
    {
        var shell = (IWshShell)Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath)!;
        shortcut.Save();
        Marshal.ReleaseComObject(shortcut);
        Marshal.ReleaseComObject(shell);
    }

    [ComImport, Guid("F935DC21-1CF0-11D0-ADB9-00C04FD58A0B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IWshShell
    {
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object CreateShortcut(string pathLink);
    }

    [ComImport, Guid("F935DC23-1CF0-11D0-ADB9-00C04FD58A0B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IWshShortcut
    {
        string TargetPath { get; set; }
        string Arguments { get; set; }
        string WorkingDirectory { get; set; }
        void Save();
    }
}
