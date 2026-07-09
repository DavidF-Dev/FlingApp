using Fling.Net;

namespace Fling.Tests;

public sealed class UdpDiscoveryTests
{
    [Fact]
    public void ParseResponse_ValidResponse_ReturnsDevice()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:7291:Pixel 8");
        var result = UdpDiscovery.ParseResponse(data, "192.168.1.50");

        Assert.NotNull(result);
        Assert.Equal("Pixel 8", result.Name);
        Assert.Equal("192.168.1.50", result.Host);
        Assert.Equal(7291, result.Port);
    }

    [Fact]
    public void ParseResponse_NameWithColons_ParsesGreedily()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:7291:My:Cool:Device");
        var result = UdpDiscovery.ParseResponse(data, "10.0.0.1");

        Assert.NotNull(result);
        Assert.Equal("My:Cool:Device", result.Name);
        Assert.Equal(7291, result.Port);
    }

    [Fact]
    public void ParseResponse_CustomPort_ParsesCorrectly()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:8080:Galaxy S24");
        var result = UdpDiscovery.ParseResponse(data, "10.0.0.5");

        Assert.NotNull(result);
        Assert.Equal(8080, result.Port);
        Assert.Equal("Galaxy S24", result.Name);
    }

    [Fact]
    public void ParseResponse_NotFlingPrefix_ReturnsNull()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("HELLO:7291:Device");
        Assert.Null(UdpDiscovery.ParseResponse(data, "10.0.0.1"));
    }

    [Fact]
    public void ParseResponse_JustFlingQuestion_ReturnsNull()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING?");
        Assert.Null(UdpDiscovery.ParseResponse(data, "10.0.0.1"));
    }

    [Fact]
    public void ParseResponse_MissingName_ReturnsNull()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:7291:");
        Assert.Null(UdpDiscovery.ParseResponse(data, "10.0.0.1"));
    }

    [Fact]
    public void ParseResponse_MissingPort_ReturnsNull()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:");
        Assert.Null(UdpDiscovery.ParseResponse(data, "10.0.0.1"));
    }

    [Fact]
    public void ParseResponse_InvalidPort_ReturnsNull()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:notaport:Device");
        Assert.Null(UdpDiscovery.ParseResponse(data, "10.0.0.1"));
    }

    [Fact]
    public void ParseResponse_PortOutOfRange_ReturnsNull()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:99999:Device");
        Assert.Null(UdpDiscovery.ParseResponse(data, "10.0.0.1"));
    }

    [Fact]
    public void ParseResponse_PortZero_ReturnsNull()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:0:Device");
        Assert.Null(UdpDiscovery.ParseResponse(data, "10.0.0.1"));
    }

    [Fact]
    public void ParseResponse_WhitespaceInName_Trimmed()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("FLING:7291:  Pixel 8  ");
        var result = UdpDiscovery.ParseResponse(data, "10.0.0.1");

        Assert.NotNull(result);
        Assert.Equal("Pixel 8", result.Name);
    }

    [Fact]
    public void ParseResponse_EmptyData_ReturnsNull()
    {
        Assert.Null(UdpDiscovery.ParseResponse([], "10.0.0.1"));
    }
}
