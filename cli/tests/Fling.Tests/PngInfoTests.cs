using System.Drawing;
using System.Drawing.Imaging;
using Fling.Content;

namespace Fling.Tests;

public sealed class PngInfoTests
{
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(16, 9)]
    [InlineData(1920, 1080)]
    public void TryGetDimensions_RealPng_ReadsSize(int width, int height)
    {
        Assert.True(PngInfo.TryGetDimensions(Png(width, height), out var w, out var h));
        Assert.Equal(width, w);
        Assert.Equal(height, h);
    }

    [Fact]
    public void TryGetDimensions_NotAPng_Fails()
    {
        Assert.False(PngInfo.TryGetDimensions("this is not a png at all"u8, out _, out _));
    }

    [Fact]
    public void TryGetDimensions_TooShort_Fails()
    {
        Assert.False(PngInfo.TryGetDimensions([0x89, 0x50, 0x4E, 0x47], out _, out _));
    }

    [Fact]
    public void TryGetDimensions_Empty_Fails()
    {
        Assert.False(PngInfo.TryGetDimensions([], out _, out _));
    }

    [Fact]
    public void TryGetDimensions_ZeroSized_Fails()
    {
        var png = Png(4, 4);
        Array.Clear(png, 16, 8);

        Assert.False(PngInfo.TryGetDimensions(png, out _, out _));
    }
}
