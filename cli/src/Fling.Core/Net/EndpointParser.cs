using System.Net;

namespace Fling.Net;

public static class EndpointParser
{
    public const int DefaultPort = 7291;

    /// <summary>
    /// Parses an endpoint string into a host and port.
    /// Supports: "192.168.1.50", "192.168.1.50:7291", "[::1]:7291".
    /// </summary>
    public static (string Host, int Port) Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Endpoint cannot be empty.");

        // IPv6 with port: [::1]:7291
        if (input.StartsWith('['))
        {
            var closeBracket = input.IndexOf(']');
            if (closeBracket < 0)
                throw new FormatException($"Invalid endpoint '{input}': missing closing bracket for IPv6 address.");

            var host = input[1..closeBracket];
            if (!IPAddress.TryParse(host, out _))
                throw new FormatException($"Invalid IPv6 address: '{host}'.");

            if (closeBracket == input.Length - 1)
                return (host, DefaultPort);

            if (input[closeBracket + 1] != ':')
                throw new FormatException($"Invalid endpoint '{input}': expected ':' after closing bracket.");

            var portStr = input[(closeBracket + 2)..];
            return (host, ParsePort(portStr, input));
        }

        // Count colons to distinguish IPv6-without-brackets from host:port
        var colonCount = input.Count(c => c == ':');

        if (colonCount == 0)
        {
            // Plain hostname or IPv4: "192.168.1.50"
            return (input, DefaultPort);
        }

        if (colonCount == 1)
        {
            // host:port
            var parts = input.Split(':');
            return (parts[0], ParsePort(parts[1], input));
        }

        // Multiple colons without brackets — bare IPv6 address
        if (IPAddress.TryParse(input, out _))
            return (input, DefaultPort);

        throw new FormatException($"Invalid endpoint '{input}': ambiguous IPv6 address. Use bracket notation: [{input}]:port");
    }

    private static int ParsePort(string portStr, string fullInput)
    {
        if (!int.TryParse(portStr, out var port) || port is < 1 or > 65535)
            throw new FormatException($"Invalid port in '{fullInput}': port must be 1–65535.");

        return port;
    }
}
