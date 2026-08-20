using System.CommandLine;
using Fling.Platform;

namespace Fling.Commands;

public static class InstallCommand
{
    public static Command Create()
    {
        var command = new Command("install", "Add Fling to the Windows 'Send to' menu");

        command.SetAction(_ =>
        {
            var shortcutPath = SendToIntegration.GetShortcutPath();
            if (shortcutPath is null)
            {
                Console.Error.WriteLine("Could not locate the SendTo directory.");
                return 1;
            }

            var exePath = ResolveExePath();
            SendToIntegration.Install(shortcutPath, exePath, "send --all --file");

            Console.WriteLine($"Installed: {shortcutPath}");
            Console.WriteLine($"Target:    {exePath}");
            Console.WriteLine("Right-click a file in Explorer and choose Send to > Fling.");
            return 0;
        });

        return command;
    }

    internal static string ResolveExePath() => SendToIntegration.ResolveExePath();
}
