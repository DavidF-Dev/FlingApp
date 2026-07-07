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
        command.Subcommands.Add(CreateDefaultCommand(store));
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

        command.Options.Add(maxSizeOption);
        command.Options.Add(compressOption);

        command.SetAction((Func<ParseResult, int>)(parseResult =>
        {
            var maxSize = parseResult.GetValue(maxSizeOption);
            var compress = parseResult.GetValue(compressOption);

            if (maxSize is null && compress is null)
            {
                Console.Error.WriteLine("No settings specified. Use --max-size or --compress.");
                return 1;
            }

            if (maxSize is <= 0)
            {
                Console.Error.WriteLine("--max-size must be greater than 0.");
                return 1;
            }

            var config = store.Load();

            if (maxSize is not null)
                config.MaxSizeMb = maxSize.Value;

            if (compress is not null)
                config.Compress = compress.Value;

            store.Save(config);
            Console.WriteLine("Configuration updated.");
            PrintConfig(config);
            return 0;
        }));

        return command;
    }

    private static Command CreateDefaultCommand(ConfigStore store)
    {
        var nameArg = new Argument<string>("device-name")
        {
            Description = "Name of the device to set as default",
        };

        var command = new Command("default", "Set a device as the default target");
        command.Arguments.Add(nameArg);

        command.SetAction((Func<ParseResult, int>)(parseResult =>
        {
            var name = parseResult.GetValue(nameArg);
            var config = store.Load();

            var device = config.Devices.Find(d =>
                d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (device is null)
            {
                Console.Error.WriteLine($"No paired device named '{name}'.");
                return 1;
            }

            foreach (var d in config.Devices)
                d.Default = false;

            device.Default = true;
            store.Save(config);
            Console.WriteLine($"Default device set to '{device.Name}'.");
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
            var config = store.Load();

            var removed = config.Devices.RemoveAll(d =>
                d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
            {
                Console.Error.WriteLine($"No paired device named '{name}'.");
                return 1;
            }

            store.Save(config);
            Console.WriteLine($"Device '{name}' removed.");
            return 0;
        }));

        return command;
    }

    internal static void PrintConfig(FlingConfig config)
    {
        Console.WriteLine($"Max size:  {config.MaxSizeMb} MB");
        Console.WriteLine($"Compress:  {config.Compress}");
        Console.WriteLine();

        if (config.Devices.Count == 0)
        {
            Console.WriteLine("No paired devices.");
            return;
        }

        Console.WriteLine($"Devices ({config.Devices.Count}):");
        foreach (var device in config.Devices)
        {
            var defaultMarker = device.Default ? " (default)" : "";
            Console.WriteLine($"  {device.Name}{defaultMarker}");
            Console.WriteLine($"    {device.Host}:{device.Port}");
        }
    }
}
