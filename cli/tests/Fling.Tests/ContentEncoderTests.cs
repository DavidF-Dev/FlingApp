using System.IO.Compression;
using System.Text;
using Fling.Content;

namespace Fling.Tests;

public sealed class ContentEncoderTests
{
    private readonly ContentEncoder _encoder = new(compressEnabled: true, maxSizeMb: 10);
    private readonly ContentEncoder _noCompress = new(compressEnabled: false, maxSizeMb: 10);

    [Fact]
    public void Encode_TextPlain_CompressesAndBase64Encodes()
    {
        var raw = Encoding.UTF8.GetBytes("Hello, world!");

        var payload = _encoder.Encode("text/plain", raw);

        Assert.Equal("text/plain", payload.Type);
        Assert.True(payload.Compressed);

        var decoded = Convert.FromBase64String(payload.Data);
        var decompressed = GZipDecompress(decoded);
        Assert.Equal(raw, decompressed);
    }

    [Fact]
    public void Encode_TextHtml_CompressesAndBase64Encodes()
    {
        var raw = Encoding.UTF8.GetBytes("<b>hello</b>");

        var payload = _encoder.Encode("text/html", raw);

        Assert.True(payload.Compressed);
        var decoded = GZipDecompress(Convert.FromBase64String(payload.Data));
        Assert.Equal(raw, decoded);
    }

    [Fact]
    public void Encode_ImagePng_NoCompression()
    {
        var raw = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var payload = _encoder.Encode("image/png", raw);

        Assert.Equal("image/png", payload.Type);
        Assert.False(payload.Compressed);
        Assert.Equal(raw, Convert.FromBase64String(payload.Data));
    }

    [Fact]
    public void Encode_ImagePng_NoCompressionEvenWhenConfigEnabled()
    {
        var raw = new byte[] { 0x89, 0x50 };

        var payload = _encoder.Encode("image/png", raw);

        Assert.False(payload.Compressed);
    }

    [Fact]
    public void Encode_TextWithCompressionDisabled_NoCompression()
    {
        var raw = Encoding.UTF8.GetBytes("Some text");

        var payload = _noCompress.Encode("text/plain", raw);

        Assert.False(payload.Compressed);
        Assert.Equal(raw, Convert.FromBase64String(payload.Data));
    }

    [Fact]
    public void Encode_ExceedsMaxSize_Throws()
    {
        var small = new ContentEncoder(compressEnabled: true, maxSizeMb: 1);
        var raw = new byte[2 * 1024 * 1024];

        var ex = Assert.Throws<ContentTooLargeException>(() =>
            small.Encode("text/plain", raw));
        Assert.Contains("MB", ex.Message);
    }

    [Fact]
    public void Encode_ExactlyMaxSize_Succeeds()
    {
        var small = new ContentEncoder(compressEnabled: false, maxSizeMb: 1);
        var raw = new byte[1 * 1024 * 1024];

        var payload = small.Encode("image/png", raw);

        Assert.NotNull(payload);
    }

    [Fact]
    public void Encode_SetsTimestamp()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = _encoder.Encode("text/plain", "hi"u8.ToArray());
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.InRange(payload.Timestamp, before, after);
    }

    [Fact]
    public void Encode_Base64RoundTrip_PreservesData()
    {
        var raw = Encoding.UTF8.GetBytes("Round trip test with unicode: 你好世界 🌍");

        var payload = _encoder.Encode("text/plain", raw);
        var decoded = GZipDecompress(Convert.FromBase64String(payload.Data));

        Assert.Equal(raw, decoded);
    }

    [Fact]
    public void Encode_Unicode_PreservesContent()
    {
        var raw = Encoding.UTF8.GetBytes("こんにちは世界");

        var payload = _noCompress.Encode("text/plain", raw);

        Assert.Equal(raw, Convert.FromBase64String(payload.Data));
    }

    private static byte[] GZipDecompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
