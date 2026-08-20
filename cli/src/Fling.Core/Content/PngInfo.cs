using System.Buffers.Binary;

namespace Fling.Content;

/// <summary>
/// Reads a PNG's dimensions from its header, without decoding the image.
/// </summary>
public static class PngInfo
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const int IhdrWidthOffset = 16;
    private const int MinimumLength = 24;

    public static bool TryGetDimensions(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (png.Length < MinimumLength || !png[..Signature.Length].SequenceEqual(Signature))
            return false;

        // IHDR is required to be the first chunk, so width and height sit at fixed offsets.
        var w = BinaryPrimitives.ReadUInt32BigEndian(png[IhdrWidthOffset..]);
        var h = BinaryPrimitives.ReadUInt32BigEndian(png[(IhdrWidthOffset + 4)..]);

        if (w is 0 or > int.MaxValue || h is 0 or > int.MaxValue)
            return false;

        width = (int)w;
        height = (int)h;
        return true;
    }
}
