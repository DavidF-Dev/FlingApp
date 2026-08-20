using System.Drawing;
using System.Drawing.Imaging;

namespace Fling.Content;

public static class ImageLoader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PngTrailer = [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    public static byte[] LoadAsPng(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Image file not found: {filePath}");

        if (IsIntactPng(filePath))
            return File.ReadAllBytes(filePath);

        using var image = Image.FromFile(filePath);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// Reports whether a file is already a complete PNG, in which case re-encoding it
    /// would cost time and usually produce a larger payload.
    /// </summary>
    /// <remarks>
    /// Checks the signature and the terminating IEND chunk rather than the file
    /// extension: a mislabelled or truncated file must still fail here rather than
    /// reach the device as a broken image.
    /// </remarks>
    private static bool IsIntactPng(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < PngSignature.Length + PngTrailer.Length)
                return false;

            Span<byte> head = stackalloc byte[8];
            stream.ReadExactly(head);
            if (!head.SequenceEqual(PngSignature))
                return false;

            Span<byte> tail = stackalloc byte[12];
            stream.Seek(-PngTrailer.Length, SeekOrigin.End);
            stream.ReadExactly(tail);
            return tail.SequenceEqual(PngTrailer);
        }
        catch (IOException)
        {
            return false;
        }
    }
}

/// <summary>
/// Loads images through GDI+.
/// </summary>
public sealed class GdiImageEncoder : IImageEncoder
{
    public byte[] LoadAsPng(string filePath) => ImageLoader.LoadAsPng(filePath);
}
