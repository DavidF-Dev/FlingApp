using System.CommandLine;
using Fling.Config;
using Fling.Net;

namespace Fling.Commands;

public static class PairCommand
{
    public static Command Create(ConfigStore store)
    {
        var endpointArg = new Argument<string>("endpoint")
        {
            Description = "Device address as ip:port (port defaults to 7291)",
        };

        var nameOption = new Option<string?>("--name")
        {
            Description = "PC name sent to the device (defaults to config hostName or machine name)",
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Re-pair even if a device with the same name or address exists",
        };

        var command = new Command("pair", "Pair with a new Android device");
        command.Arguments.Add(endpointArg);
        command.Options.Add(nameOption);
        command.Options.Add(forceOption);

        command.SetAction((Func<ParseResult, CancellationToken, Task<int>>)(async (parseResult, ct) =>
        {
            var endpoint = parseResult.GetValue(endpointArg)!;
            var nameOverride = parseResult.GetValue(nameOption);
            var force = parseResult.GetValue(forceOption);

            string host;
            int port;
            try
            {
                (host, port) = EndpointParser.Parse(endpoint);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            var config = store.Load();
            var pcName = nameOverride
                         ?? (string.IsNullOrEmpty(config.HostName) ? Environment.MachineName : config.HostName);

            var existingByAddress = config.Devices.Find(d =>
                d.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && d.Port == port);

            if (existingByAddress is not null && !force)
            {
                Console.Error.WriteLine(
                    $"A device at {host}:{port} is already paired as '{existingByAddress.Name}'. Use --force to re-pair.");
                return 1;
            }

            var apiKey = ApiKeyGenerator.Generate();

            Console.WriteLine($"Pairing with {host}:{port} as '{pcName}'...");
            Console.WriteLine("Waiting for approval on the device...");

            PairResponse response;
            try
            {
                using var client = new FlingHttpClient();
                response = await client.PairAsync(host, port, pcName, apiKey, ct);
            }
            catch (TaskCanceledException)
            {
                Console.Error.WriteLine("Pairing timed out. Make sure the Fling app is running on the device.");
                return 2;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Could not connect to {host}:{port}: {ex.Message}");
                return 2;
            }

            if (response.Status != "accepted")
            {
                Console.Error.WriteLine("Pairing was rejected on the device.");
                return 1;
            }

            var existingByName = config.Devices.Find(d =>
                d.Name.Equals(response.Name, StringComparison.OrdinalIgnoreCase));

            if (existingByName is not null && !ReferenceEquals(existingByName, existingByAddress) && !force)
            {
                Console.Error.WriteLine(
                    $"A device named '{response.Name}' already exists at {existingByName.Host}:{existingByName.Port}. Use --force to re-pair.");
                return 1;
            }

            // Remove any existing entries that match by address or name
            config.Devices.RemoveAll(d =>
                (d.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && d.Port == port) ||
                d.Name.Equals(response.Name, StringComparison.OrdinalIgnoreCase));

            config.Devices.Add(new DeviceConfig
            {
                Name = response.Name,
                Host = host,
                Port = port,
                ApiKey = apiKey,
            });

            store.Save(config);
            Console.WriteLine($"Paired with '{response.Name}' at {host}:{port}.");
            return 0;
        }));

        return command;
    }
}
