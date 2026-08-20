using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;

namespace Fling.Content;

/// <summary>
/// Converts a device-independent bitmap, as delivered by the clipboard, into PNG bytes.
/// </summary>
internal static class DibConverter
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const uint BI_BITFIELDS = 3;
    private const uint BI_ALPHABITFIELDS = 6;

    /// <summary>
    /// A DIB is a BMP file without its 14-byte file header. Prepending one produces a
    /// stream any BMP decoder accepts, which avoids hand-rolling pixel handling for the
    /// several bit depths, palettes, and row orders a DIB may use.
    /// </summary>
    public static byte[] ToPng(byte[] dib)
    {
        if (dib.Length < InfoHeaderSize)
            throw new InvalidOperationException("Clipboard bitmap header is truncated.");

        var pixelOffset = FileHeaderSize + PixelDataOffsetWithinDib(dib);

        var bmp = new byte[FileHeaderSize + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), pixelOffset);
        dib.CopyTo(bmp, FileHeaderSize);

        using var source = new MemoryStream(bmp, writable: false);
        using var image = Image.FromStream(source);
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static int PixelDataOffsetWithinDib(byte[] dib)
    {
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(0));
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        var colorsUsed = (int)BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));

        if (headerSize < InfoHeaderSize || headerSize > dib.Length)
            throw new InvalidOperationException("Clipboard bitmap header is malformed.");

        var offset = headerSize;

        // Only V4 and V5 headers carry the channel masks inline; a plain info header
        // stores them in the space a palette would otherwise occupy.
        if (headerSize == InfoHeaderSize)
        {
            if (compression == BI_BITFIELDS)
                offset += 12;
            else if (compression == BI_ALPHABITFIELDS)
                offset += 16;
        }

        if (bitCount <= 8)
        {
            var paletteEntries = colorsUsed != 0 ? colorsUsed : 1 << bitCount;
            offset += paletteEntries * 4;
        }

        return offset;
    }
}
