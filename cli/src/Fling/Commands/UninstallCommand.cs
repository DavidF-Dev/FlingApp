using System.CommandLine;
using Fling.Platform;

namespace Fling.Commands;

public static class UninstallCommand
{
    public static Command Create()
    {
        var command = new Command("uninstall", "Remove Fling from the Windows 'Send to' menu");

        command.SetAction(_ =>
        {
            var shortcutPath = SendToIntegration.GetShortcutPath();
            if (shortcutPath is null)
            {
                Console.Error.WriteLine("Could not locate the SendTo directory.");
                return 1;
            }

            if (!File.Exists(shortcutPath))
            {
                Console.WriteLine("Fling is not installed in the Send to menu.");
                return 0;
            }

            File.Delete(shortcutPath);
            Console.WriteLine($"Removed: {shortcutPath}");
            return 0;
        });

        return command;
    }
}
