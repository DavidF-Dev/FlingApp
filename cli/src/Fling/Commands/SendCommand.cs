using System.CommandLine;
using Fling.Config;
using Fling.Content;
using Fling.Net;
using Fling.Operations;

namespace Fling.Commands;

public static class SendCommand
{
    public static Command Create(ConfigStore store, IClipboardReader clipboardReader, DiscoveryCache? discoveryCache = null, UdpDiscovery? udpDiscovery = null)
        => Create(store, clipboardReader, new GdiImageEncoder(), discoveryCache, udpDiscovery);

    public static Command Create(ConfigStore store, IClipboardReader clipboardReader, IImageEncoder imageEncoder, DiscoveryCache? discoveryCache = null, UdpDiscovery? udpDiscovery = null)
    {
        var clipboardOption = new Option<bool>("--clipboard")
        {
            Description = "Send current clipboard contents",
        };
        var imageOption = new Option<string?>("--image")
        {
            Description = "Send an image file (converted to PNG)",
        };
        var textOption = new Option<string?>("--text")
        {
            Description = "Send literal text",
        };
        var fileOption = new Option<string?>("--file")
        {
            Description = "Send a file (auto-detects image vs text)",
        };
        var deviceOption = new Option<string?>("--device")
        {
            Description = "Target device name",
        };
        var allOption = new Option<bool>("--all")
        {
            Description = "Send to all paired devices",
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Encode content and print details without sending",
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Print request/response details",
        };

        var command = new Command("send", "Send content to a paired device");
        command.Options.Add(clipboardOption);
        command.Options.Add(imageOption);
        command.Options.Add(textOption);
        command.Options.Add(fileOption);
        command.Options.Add(deviceOption);
        command.Options.Add(allOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(verboseOption);

        command.SetAction((Func<ParseResult, CancellationToken, Task<int>>)(async (parseResult, ct) =>
        {
            var useClipboard = parseResult.GetValue(clipboardOption);
            var imagePath = parseResult.GetValue(imageOption);
            var text = parseResult.GetValue(textOption);
            var filePath = parseResult.GetValue(fileOption);
            var deviceName = parseResult.GetValue(deviceOption);
            var all = parseResult.GetValue(allOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var verbose = parseResult.GetValue(verboseOption);

            var config = store.Load();

            var sourceCount = (useClipboard ? 1 : 0) + (imagePath is not null ? 1 : 0) + (text is not null ? 1 : 0) + (filePath is not null ? 1 : 0);
            if (sourceCount == 0)
            {
                Console.Error.WriteLine("No content source specified. Use --clipboard, --image <path>, --text \"content\", or --file <path>.");
                return 1;
            }
            if (sourceCount > 1)
            {
                Console.Error.WriteLine("Specify only one content source: --clipboard, --image, --text, or --file.");
                return 1;
            }

            var resolver = new ContentResolver(clipboardReader, imageEncoder);
            ResolvedContent content;
            try
            {
                content = useClipboard ? resolver.FromClipboard()
                    : imagePath is not null ? resolver.FromImage(imagePath)
                    : filePath is not null ? resolver.FromFile(filePath)
                    : ContentResolver.FromText(text!);
            }
            catch (ContentResolutionException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            ClipPayload payload;
            try
            {
                payload = SendOperation.Encode(config, content);
            }
            catch (ContentTooLargeException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            if (verbose || dryRun)
            {
                Console.WriteLine($"Content type: {payload.Type}");
                Console.WriteLine($"Raw size:     {content.Data.Length:N0} bytes");
                Console.WriteLine($"Encoded size: {payload.Data.Length:N0} chars (base64)");
                Console.WriteLine($"Compressed:   {payload.Compressed}");
            }

            if (dryRun)
            {
                Console.WriteLine("Dry run — not sending.");
                return 0;
            }

            List<DeviceConfig> devices;
            DeviceResolver deviceResolver;
            try
            {
                deviceResolver = discoveryCache is not null && udpDiscovery is not null
                    ? new DeviceResolver(config, store, discoveryCache, udpDiscovery)
                    : new DeviceResolver(config);
                devices = deviceResolver.Resolve(deviceName, all);
            }
            catch (DeviceResolutionException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            await deviceResolver.ResolveAddressesAsync(devices, ct);

            var results = await new SendOperation(store).SendAsync(
                config,
                devices,
                payload,
                onSending: verbose
                    ? device => Console.WriteLine($"Sending to {device.Name} ({device.Host}:{device.Port})...")
                    : null,
                ct);

            var hasAuthFailure = false;
            var hasNetworkFailure = false;
            foreach (var result in results)
            {
                if (result.Success)
                {
                    Console.WriteLine($"Sent to '{result.Device.Name}'.");
                }
                else
                {
                    Console.Error.WriteLine($"Failed to send to '{result.Device.Name}': {result.Error}");
                    if (result.AuthFailed)
                        hasAuthFailure = true;
                    else
                        hasNetworkFailure = true;
                }
            }

            if (hasAuthFailure) return 3;
            if (hasNetworkFailure) return 2;
            return 0;
        }));

        return command;
    }
}
