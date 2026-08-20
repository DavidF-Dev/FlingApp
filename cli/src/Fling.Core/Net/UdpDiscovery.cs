using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Fling.Net;

/// <summary>
/// Discovers Fling devices on the local network via UDP broadcast.
/// Sends "FLING?" to the broadcast address; devices respond with "FLING:&lt;port&gt;:&lt;name&gt;".
/// </summary>
public sealed class UdpDiscovery : IDeviceDiscovery
{
    public const int DiscoveryPort = 7290;
    private static readonly byte[] DiscoveryMessage = Encoding.UTF8.GetBytes("FLING?");

    private readonly TimeSpan _timeout;

    public UdpDiscovery(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMilliseconds(1500);
    }

    public async Task<List<DiscoveredDevice>> DiscoverAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        await udp.SendAsync(DiscoveryMessage, DiscoveryMessage.Length, broadcastEndpoint);

        var devices = new List<DiscoveredDevice>();
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cts.Token);
                var device = ParseResponse(result.Buffer, result.RemoteEndPoint.Address.ToString());
                if (device is not null)
                    devices.Add(device);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached — return whatever we collected.
        }

        return devices;
    }

    internal static DiscoveredDevice? ParseResponse(byte[] data, string remoteHost)
    {
        var response = Encoding.UTF8.GetString(data);

        if (!response.StartsWith("FLING:", StringComparison.Ordinal))
            return null;

        // Format: FLING:<port>:<name> — greedy split, name may contain colons.
        var firstColon = "FLING:".Length;
        var secondColon = response.IndexOf(':', firstColon);
        if (secondColon < 0 || secondColon + 1 >= response.Length)
            return null;

        var portStr = response[firstColon..secondColon];
        if (!int.TryParse(portStr, out var port) || port is < 1 or > 65535)
            return null;

        var name = response[(secondColon + 1)..].Trim();
        if (name.Length == 0)
            return null;

        return new DiscoveredDevice(name, remoteHost, port);
    }
}

public sealed record DiscoveredDevice(string Name, string Host, int Port);
