using System.CommandLine;
using Fling.Config;
using Fling.Net;
using Fling.Operations;

namespace Fling.Commands;

public static class PairCommand
{
    public static Command Create(ConfigStore store)
    {
        var endpointArg = new Argument<string?>("endpoint")
        {
            Description = "Device address as ip:port (port defaults to 7291)",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var nameOption = new Option<string?>("--name")
        {
            Description = "PC name sent to the device (defaults to config hostName or machine name)",
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Re-pair even if a device with the same name or address exists",
        };

        var discoverOption = new Option<bool>("--discover")
        {
            Description = "Find devices on the local network via UDP broadcast",
        };

        var command = new Command("pair", "Pair with a new Android device");
        command.Arguments.Add(endpointArg);
        command.Options.Add(nameOption);
        command.Options.Add(forceOption);
        command.Options.Add(discoverOption);

        command.SetAction((Func<ParseResult, CancellationToken, Task<int>>)(async (parseResult, ct) =>
        {
            var endpoint = parseResult.GetValue(endpointArg);
            var nameOverride = parseResult.GetValue(nameOption);
            var force = parseResult.GetValue(forceOption);
            var discover = parseResult.GetValue(discoverOption);

            if (endpoint is null && !discover)
            {
                Console.Error.WriteLine("Specify a device address or use --discover to find devices on the network.");
                return 1;
            }

            string host;
            int port;

            if (discover)
            {
                var discovery = new UdpDiscovery();
                Console.WriteLine("Searching for devices on the local network...");
                var found = await discovery.DiscoverAsync(ct);

                if (found.Count == 0)
                {
                    Console.Error.WriteLine("No devices found. Make sure the Fling app is running on your phone and both devices are on the same Wi-Fi network.");
                    return 2;
                }

                if (found.Count == 1)
                {
                    host = found[0].Host;
                    port = found[0].Port;
                    Console.WriteLine($"Found '{found[0].Name}' at {host}:{port}.");
                }
                else
                {
                    Console.WriteLine("Multiple devices found:");
                    foreach (var d in found)
                        Console.WriteLine($"  {d.Name}  ({d.Host}:{d.Port})");
                    Console.Error.WriteLine("Specify a device address: fling pair <ip:port>");
                    return 1;
                }
            }
            else
            {
                try
                {
                    (host, port) = EndpointParser.Parse(endpoint!);
                }
                catch (FormatException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            var config = store.Load();
            var pcName = nameOverride
                         ?? (string.IsNullOrEmpty(config.HostName) ? Environment.MachineName : config.HostName);

            // The address conflict is reported before the request so the user is not left
            // waiting on an approval prompt for a pairing that cannot be stored.
            if (!force && config.Devices.Exists(d =>
                    d.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && d.Port == port))
            {
                var existing = config.Devices.Find(d =>
                    d.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && d.Port == port)!;
                Console.Error.WriteLine(
                    $"A device at {host}:{port} is already paired as '{existing.Name}'. Use --force to re-pair.");
                return 1;
            }

            Console.WriteLine($"Pairing with {host}:{port} as '{pcName}'...");
            Console.WriteLine("Waiting for approval on the device...");

            var outcome = await new PairOperation(store).ExecuteAsync(config, host, port, pcName, force, ct);

            switch (outcome.Status)
            {
                case PairStatus.Accepted:
                    Console.WriteLine($"Paired with '{outcome.DeviceName}' at {host}:{port}.");
                    return 0;

                case PairStatus.TimedOut:
                case PairStatus.ConnectionFailed:
                    Console.Error.WriteLine(outcome.Error);
                    return 2;

                default:
                    Console.Error.WriteLine(outcome.Error);
                    return 1;
            }
        }));

        return command;
    }
}
