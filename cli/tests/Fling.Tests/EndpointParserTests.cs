using Fling.Net;

namespace Fling.Tests;

public sealed class EndpointParserTests
{
    [Fact]
    public void Parse_IpWithPort()
    {
        var (host, port) = EndpointParser.Parse("192.168.1.50:7291");

        Assert.Equal("192.168.1.50", host);
        Assert.Equal(7291, port);
    }

    [Fact]
    public void Parse_IpWithoutPort_DefaultsTo7291()
    {
        var (host, port) = EndpointParser.Parse("192.168.1.50");

        Assert.Equal("192.168.1.50", host);
        Assert.Equal(EndpointParser.DefaultPort, port);
    }

    [Fact]
    public void Parse_IpWithCustomPort()
    {
        var (host, port) = EndpointParser.Parse("10.0.0.1:8080");

        Assert.Equal("10.0.0.1", host);
        Assert.Equal(8080, port);
    }

    [Fact]
    public void Parse_IPv6WithBracketsAndPort()
    {
        var (host, port) = EndpointParser.Parse("[::1]:7291");

        Assert.Equal("::1", host);
        Assert.Equal(7291, port);
    }

    [Fact]
    public void Parse_IPv6WithBracketsNoPort()
    {
        var (host, port) = EndpointParser.Parse("[::1]");

        Assert.Equal("::1", host);
        Assert.Equal(EndpointParser.DefaultPort, port);
    }

    [Fact]
    public void Parse_BareIPv6_DefaultsPort()
    {
        var (host, port) = EndpointParser.Parse("::1");

        Assert.Equal("::1", host);
        Assert.Equal(EndpointParser.DefaultPort, port);
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => EndpointParser.Parse(""));
    }

    [Fact]
    public void Parse_Whitespace_Throws()
    {
        Assert.Throws<FormatException>(() => EndpointParser.Parse("   "));
    }

    [Fact]
    public void Parse_InvalidPort_Throws()
    {
        Assert.Throws<FormatException>(() => EndpointParser.Parse("192.168.1.50:99999"));
    }

    [Fact]
    public void Parse_ZeroPort_Throws()
    {
        Assert.Throws<FormatException>(() => EndpointParser.Parse("192.168.1.50:0"));
    }

    [Fact]
    public void Parse_NonNumericPort_Throws()
    {
        Assert.Throws<FormatException>(() => EndpointParser.Parse("192.168.1.50:abc"));
    }
}
