using System.CommandLine;
using System.Diagnostics;
using Fling.Config;
using Fling.Net;

namespace Fling.Commands;

public static class StatusCommand
{
    public static Command Create(ConfigStore store)
    {
        var deviceOption = new Option<string?>("--device")
        {
            Description = "Check a single device by name",
        };

        var command = new Command("status", "Check reachability of paired devices");
        command.Options.Add(deviceOption);

        command.SetAction((Func<ParseResult, CancellationToken, Task<int>>)(async (parseResult, ct) =>
        {
            var deviceName = parseResult.GetValue(deviceOption);
            var config = store.Load();

            if (config.Devices.Count == 0)
            {
                Console.Error.WriteLine("No paired devices. Run 'fling pair <ip:port>' first.");
                return 1;
            }

            var devices = config.Devices;
            if (deviceName is not null)
            {
                var device = devices.Find(d =>
                    d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

                if (device is null)
                {
                    Console.Error.WriteLine($"No paired device named '{deviceName}'.");
                    return 1;
                }

                devices = [device];
            }

            using var client = new FlingHttpClient();
            var tasks = devices.Select(async device =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var response = await client.PingAsync(device.Host, device.Port, device.ApiKey, ct);
                    sw.Stop();
                    return new DeviceStatus(device, true, response.Version, sw.ElapsedMilliseconds, null);
                }
                catch (TaskCanceledException)
                {
                    return new DeviceStatus(device, false, null, null, "timeout");
                }
                catch (HttpRequestException ex)
                {
                    return new DeviceStatus(device, false, null, null, ex.Message);
                }
            });

            var results = await Task.WhenAll(tasks);
            PrintTable(results);

            return results.All(r => r.Online) ? 0 : 2;
        }));

        return command;
    }

    private static void PrintTable(DeviceStatus[] results)
    {
        var nameWidth = Math.Max("DEVICE".Length, results.Max(r => r.Device.Name.Length));
        var addressWidth = Math.Max("ADDRESS".Length, results.Max(r => $"{r.Device.Host}:{r.Device.Port}".Length));

        Console.WriteLine(
            $"{"DEVICE".PadRight(nameWidth)}  {"ADDRESS".PadRight(addressWidth)}  {"STATUS",-8}  {"VERSION",-10}  LATENCY");

        foreach (var r in results)
        {
            var address = $"{r.Device.Host}:{r.Device.Port}";
            var status = r.Online ? "online" : "offline";
            var version = r.Version ?? "-";
            var latency = r.LatencyMs.HasValue ? $"{r.LatencyMs}ms" : "-";

            Console.WriteLine(
                $"{r.Device.Name.PadRight(nameWidth)}  {address.PadRight(addressWidth)}  {status,-8}  {version,-10}  {latency}");
        }
    }

    private sealed record DeviceStatus(
        DeviceConfig Device,
        bool Online,
        string? Version,
        long? LatencyMs,
        string? Error);
}
