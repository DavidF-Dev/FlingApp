using System.Buffers.Binary;
using System.Text;
using Fling.Content;

namespace Fling.Tests;

public sealed class ClipboardPrecedenceTests
{
    private const uint CF_DIB = 8;
    private const uint CF_UNICODETEXT = 13;
    private const uint HtmlFormat = 49_000;

    [Fact]
    public void ReadFrom_ImageAndText_PrefersImage()
    {
        var source = new FakeClipboardSource()
            .With(CF_DIB, TestDib.Create(4, 4))
            .With(CF_UNICODETEXT, Utf16("some text"));

        var content = WindowsClipboardReader.ReadFrom(source);

        Assert.Equal("image/png", content!.ContentType);
    }

    [Fact]
    public void ReadFrom_HtmlAndPlainText_PrefersHtml()
    {
        var source = new FakeClipboardSource()
            .With(HtmlFormat, Utf8(CfHtml("<b>rich</b>")))
            .With(CF_UNICODETEXT, Utf16("rich"));

        var content = WindowsClipboardReader.ReadFrom(source);

        Assert.Equal("text/html", content!.ContentType);
        Assert.Equal("<b>rich</b>", Encoding.UTF8.GetString(content.Data));
    }

    [Fact]
    public void ReadFrom_ImageHtmlAndText_PrefersImage()
    {
        var source = new FakeClipboardSource()
            .With(CF_DIB, TestDib.Create(2, 2))
            .With(HtmlFormat, Utf8(CfHtml("<b>rich</b>")))
            .With(CF_UNICODETEXT, Utf16("rich"));

        var content = WindowsClipboardReader.ReadFrom(source);

        Assert.Equal("image/png", content!.ContentType);
    }

    [Fact]
    public void ReadFrom_PlainTextOnly_ReturnsPlainText()
    {
        var source = new FakeClipboardSource().With(CF_UNICODETEXT, Utf16("hello"));

        var content = WindowsClipboardReader.ReadFrom(source);

        Assert.Equal("text/plain", content!.ContentType);
        Assert.Equal("hello", Encoding.UTF8.GetString(content.Data));
    }

    [Fact]
    public void ReadFrom_NothingAvailable_ReturnsNull()
    {
        Assert.Null(WindowsClipboardReader.ReadFrom(new FakeClipboardSource()));
    }

    [Fact]
    public void ReadFrom_TextIsNullTerminated_TrimsTerminator()
    {
        var source = new FakeClipboardSource().With(CF_UNICODETEXT, Utf16("hello"));

        var content = WindowsClipboardReader.ReadFrom(source);

        Assert.Equal("hello", Encoding.UTF8.GetString(content!.Data));
    }

    private static byte[] Utf16(string value) => Encoding.Unicode.GetBytes(value + '\0');

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + '\0');

    private static string CfHtml(string fragment)
    {
        var header = "Version:0.9\r\nStartHTML:<<SH>>\r\nEndHTML:<<EH>>\r\nStartFragment:<<SF>>\r\nEndFragment:<<EF>>\r\n";
        var prefix = "<html><body>";
        var suffix = "</body></html>";

        var headerLength = header
            .Replace("<<SH>>", "00000000")
            .Replace("<<EH>>", "00000000")
            .Replace("<<SF>>", "00000000")
            .Replace("<<EF>>", "00000000").Length;

        var startHtml = headerLength;
        var startFragment = startHtml + prefix.Length;
        var endFragment = startFragment + fragment.Length;
        var endHtml = endFragment + suffix.Length;

        return header
                   .Replace("<<SH>>", startHtml.ToString("D8"))
                   .Replace("<<EH>>", endHtml.ToString("D8"))
                   .Replace("<<SF>>", startFragment.ToString("D8"))
                   .Replace("<<EF>>", endFragment.ToString("D8"))
               + prefix + fragment + suffix;
    }

    private sealed class FakeClipboardSource : IClipboardSource
    {
        private readonly Dictionary<uint, byte[]> _formats = [];

        public FakeClipboardSource With(uint format, byte[] data)
        {
            _formats[format] = data;
            return this;
        }

        public bool IsFormatAvailable(uint format) => _formats.ContainsKey(format);

        public byte[]? GetBytes(uint format) => _formats.GetValueOrDefault(format);

        public uint RegisterFormat(string name) => HtmlFormat;
    }
}

/// <summary>
/// Builds a bottom-up 24-bit device-independent bitmap of a solid colour.
/// </summary>
internal static class TestDib
{
    public static byte[] Create(int width, int height, byte r = 0x20, byte g = 0x40, byte b = 0x60)
    {
        var rowStride = (width * 3 + 3) & ~3;
        var dib = new byte[40 + rowStride * height];

        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(0), 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), (uint)(rowStride * height));

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = 40 + y * rowStride + x * 3;
                dib[offset] = b;
                dib[offset + 1] = g;
                dib[offset + 2] = r;
            }
        }

        return dib;
    }
}
