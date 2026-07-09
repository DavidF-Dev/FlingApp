using System.CommandLine;
using System.Text;
using Fling.Config;
using Fling.Content;
using Fling.Net;

namespace Fling.Commands;

public static class SendCommand
{
    public static Command Create(ConfigStore store, IClipboardReader clipboardReader, DiscoveryCache? discoveryCache = null, UdpDiscovery? udpDiscovery = null)
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
        command.Options.Add(deviceOption);
        command.Options.Add(allOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(verboseOption);

        command.SetAction((Func<ParseResult, CancellationToken, Task<int>>)(async (parseResult, ct) =>
        {
            var useClipboard = parseResult.GetValue(clipboardOption);
            var imagePath = parseResult.GetValue(imageOption);
            var text = parseResult.GetValue(textOption);
            var deviceName = parseResult.GetValue(deviceOption);
            var all = parseResult.GetValue(allOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var verbose = parseResult.GetValue(verboseOption);

            var config = store.Load();

            // Resolve content
            var sourceCount = (useClipboard ? 1 : 0) + (imagePath is not null ? 1 : 0) + (text is not null ? 1 : 0);
            if (sourceCount == 0)
            {
                Console.Error.WriteLine("No content source specified. Use --clipboard, --image <path>, or --text \"content\".");
                return 1;
            }
            if (sourceCount > 1)
            {
                Console.Error.WriteLine("Specify only one content source: --clipboard, --image, or --text.");
                return 1;
            }

            string contentType;
            byte[] rawBytes;

            if (useClipboard)
            {
                var content = clipboardReader.Read();
                if (content is null)
                {
                    Console.Error.WriteLine("Clipboard is empty or contains unsupported content.");
                    return 1;
                }
                contentType = content.ContentType;
                rawBytes = content.Data;
            }
            else if (imagePath is not null)
            {
                try
                {
                    rawBytes = ImageLoader.LoadAsPng(imagePath);
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine($"Image file not found: {imagePath}");
                    return 1;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not load image: {ex.Message}");
                    return 1;
                }
                contentType = "image/png";
            }
            else
            {
                rawBytes = Encoding.UTF8.GetBytes(text!);
                contentType = "text/plain";
            }

            // Encode
            var encoder = new ContentEncoder(config.Compress, config.MaxSizeMb);
            ClipPayload payload;
            try
            {
                payload = encoder.Encode(contentType, rawBytes);
            }
            catch (ContentTooLargeException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            if (verbose || dryRun)
            {
                Console.WriteLine($"Content type: {payload.Type}");
                Console.WriteLine($"Raw size:     {rawBytes.Length:N0} bytes");
                Console.WriteLine($"Encoded size: {payload.Data.Length:N0} chars (base64)");
                Console.WriteLine($"Compressed:   {payload.Compressed}");
            }

            if (dryRun)
            {
                Console.WriteLine("Dry run — not sending.");
                return 0;
            }

            // Resolve devices
            List<DeviceConfig> devices;
            DeviceResolver resolver;
            try
            {
                resolver = discoveryCache is not null && udpDiscovery is not null
                    ? new DeviceResolver(config, store, discoveryCache, udpDiscovery)
                    : new DeviceResolver(config);
                devices = resolver.Resolve(deviceName, all);
            }
            catch (DeviceResolutionException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            await resolver.ResolveAddressesAsync(devices, ct);

            var pcName = string.IsNullOrEmpty(config.HostName) ? Environment.MachineName : config.HostName;

            // Send
            using var client = new FlingHttpClient();
            var tasks = devices.Select(async device =>
            {
                if (verbose)
                    Console.WriteLine($"Sending to {device.Name} ({device.Host}:{device.Port})...");

                var result = await client.SendClipAsync(device.Host, device.Port, device.ApiKey, payload, pcName, ct);
                return (device, result);
            });

            var results = await Task.WhenAll(tasks);

            var configChanged = false;
            var hasAuthFailure = false;
            var hasNetworkFailure = false;
            foreach (var (device, result) in results)
            {
                if (result.Success)
                {
                    Console.WriteLine($"Sent to '{device.Name}'.");

                    if (result.DeviceName is not null && result.DeviceName != device.Name)
                    {
                        device.Name = result.DeviceName;
                        configChanged = true;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Failed to send to '{device.Name}': {result.Error}");
                    if (result.AuthFailed)
                        hasAuthFailure = true;
                    else
                        hasNetworkFailure = true;
                }
            }

            if (configChanged)
            {
                try { store.Save(config); }
                catch { }
            }

            if (hasAuthFailure) return 3;
            if (hasNetworkFailure) return 2;
            return 0;
        }));

        return command;
    }
}
