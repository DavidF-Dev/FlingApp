using System.CommandLine;
using Fling.Config;

namespace Fling.Commands;

public static class ConfigCommand
{
    public static Command Create(ConfigStore store)
    {
        var command = new Command("config", "Show or edit configuration");

        command.Subcommands.Add(CreateShowCommand(store));
        command.Subcommands.Add(CreateSetCommand(store));
        command.Subcommands.Add(CreateRemoveCommand(store));

        return command;
    }

    private static Command CreateShowCommand(ConfigStore store)
    {
        var command = new Command("show", "Display current configuration");

        command.SetAction(_ =>
        {
            var config = store.Load();
            PrintConfig(config);
        });

        return command;
    }

    private static Command CreateSetCommand(ConfigStore store)
    {
        var command = new Command("set", "Update configuration settings");

        var maxSizeOption = new Option<int?>("--max-size")
        {
            Description = "Maximum payload size in MB (must be > 0)",
        };

        var compressOption = new Option<bool?>("--compress")
        {
            Description = "Enable or disable GZip compression for text (true/false)",
        };

        var logOption = new Option<bool?>("--log")
        {
            Description = "Enable or disable file logging to %APPDATA%\\Fling\\fling.log (true/false)",
        };

        var hostnameOption = new Option<string?>("--hostname")
        {
            Description = "PC name sent to devices (defaults to machine name if empty)",
        };

        command.Options.Add(maxSizeOption);
        command.Options.Add(compressOption);
        command.Options.Add(logOption);
        command.Options.Add(hostnameOption);

        command.SetAction((Func<ParseResult, int>)(parseResult =>
        {
            var maxSize = parseResult.GetValue(maxSizeOption);
            var compress = parseResult.GetValue(compressOption);
            var log = parseResult.GetValue(logOption);
            var hostname = parseResult.GetValue(hostnameOption);

            if (maxSize is null && compress is null && log is null && hostname is null)
            {
                Console.Error.WriteLine("No settings specified. Use --max-size, --compress, --log, or --hostname.");
                return 1;
            }

            if (maxSize is <= 0)
            {
                Console.Error.WriteLine("--max-size must be greater than 0.");
                return 1;
            }

            var config = store.Update(c =>
            {
                if (maxSize is not null)
                    c.MaxSizeMb = maxSize.Value;

                if (compress is not null)
                    c.Compress = compress.Value;

                if (log is not null)
                    c.Log = log.Value;

                if (hostname is not null)
                    c.HostName = hostname;
            });

            Console.WriteLine("Configuration updated.");
            PrintConfig(config);
            return 0;
        }));

        return command;
    }

    private static Command CreateRemoveCommand(ConfigStore store)
    {
        var nameArg = new Argument<string>("device-name")
        {
            Description = "Name of the device to remove",
        };

        var command = new Command("remove", "Remove a paired device");
        command.Arguments.Add(nameArg);

        command.SetAction((Func<ParseResult, int>)(parseResult =>
        {
            var name = parseResult.GetValue(nameArg);

            var removed = 0;
            store.Update(c => removed = c.Devices.RemoveAll(d =>
                d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

            if (removed == 0)
            {
                Console.Error.WriteLine($"No paired device named '{name}'.");
                return 1;
            }

            Console.WriteLine($"Device '{name}' removed.");
            return 0;
        }));

        return command;
    }

    internal static void PrintConfig(FlingConfig config)
    {
        Console.WriteLine($"Max size:  {config.MaxSizeMb} MB");
        Console.WriteLine($"Compress:  {config.Compress}");
        Console.WriteLine($"Log:       {config.Log}");
        Console.WriteLine($"Hostname:  {(string.IsNullOrEmpty(config.HostName) ? $"(default: {Environment.MachineName})" : config.HostName)}");
        Console.WriteLine();

        if (config.Devices.Count == 0)
        {
            Console.WriteLine("No paired devices.");
            return;
        }

        Console.WriteLine($"Devices ({config.Devices.Count}):");
        foreach (var device in config.Devices)
        {
            Console.WriteLine($"  {device.Name}");
            Console.WriteLine($"    {device.Host}:{device.Port}");
        }
    }
}
