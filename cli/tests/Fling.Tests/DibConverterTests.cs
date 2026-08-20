using System.Drawing;
using Fling.Content;

namespace Fling.Tests;

public sealed class DibConverterTests
{
    [Fact]
    public void ToPng_ProducesPngSignature()
    {
        var png = DibConverter.ToPng(TestDib.Create(8, 4));

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
    }

    [Fact]
    public void ToPng_PreservesDimensions()
    {
        var png = DibConverter.ToPng(TestDib.Create(13, 7));

        using var stream = new MemoryStream(png);
        using var image = Image.FromStream(stream);

        Assert.Equal(13, image.Width);
        Assert.Equal(7, image.Height);
    }

    /// <summary>
    /// A bottom-up DIB decoded without accounting for row order yields a vertically
    /// mirrored image, which a solid colour would hide — so the top and bottom rows
    /// differ here.
    /// </summary>
    [Fact]
    public void ToPng_BottomUpDib_IsNotVerticallyFlipped()
    {
        var dib = TestDib.Create(2, 2);
        var rowStride = 8;

        // Bottom-up storage: the first stored row is the image's bottom row.
        WritePixel(dib, 40 + 0 * rowStride, 0x00, 0x00, 0xFF);
        WritePixel(dib, 40 + 1 * rowStride, 0xFF, 0x00, 0x00);

        var png = DibConverter.ToPng(dib);

        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);

        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x00), StripAlpha(bitmap.GetPixel(0, 0)));
        Assert.Equal(Color.FromArgb(0x00, 0x00, 0xFF), StripAlpha(bitmap.GetPixel(0, 1)));
    }

    [Fact]
    public void ToPng_TruncatedHeader_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DibConverter.ToPng(new byte[10]));
    }

    [Fact]
    public void ToPng_MalformedHeaderSize_Throws()
    {
        var dib = TestDib.Create(2, 2);
        dib[0] = 0xFF;
        dib[1] = 0xFF;

        Assert.Throws<InvalidOperationException>(() => DibConverter.ToPng(dib));
    }

    private static void WritePixel(byte[] dib, int offset, byte r, byte g, byte b)
    {
        dib[offset] = b;
        dib[offset + 1] = g;
        dib[offset + 2] = r;
    }

    private static Color StripAlpha(Color color) => Color.FromArgb(color.R, color.G, color.B);
}
