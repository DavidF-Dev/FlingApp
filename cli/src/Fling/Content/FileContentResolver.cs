using System.Text;

namespace Fling.Content;

/// <summary>
/// Determines how to send a file: as an image, as text content, or as a file path.
/// </summary>
public static class FileContentResolver
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif",
    };

    private const int BinarySampleSize = 8192;

    public static FileContent Resolve(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var info = new FileInfo(filePath);
        if (info.Length == 0)
            throw new InvalidOperationException("File is empty.");

        var ext = Path.GetExtension(filePath);
        if (ImageExtensions.Contains(ext))
            return new FileContent(FileContentKind.Image, filePath);

        if (!ContainsNullBytes(filePath))
            return new FileContent(FileContentKind.Text, filePath);

        return new FileContent(FileContentKind.FilePath, filePath);
    }

    private static bool ContainsNullBytes(string filePath)
    {
        var buffer = new byte[BinarySampleSize];
        using var stream = File.OpenRead(filePath);
        var bytesRead = stream.Read(buffer, 0, buffer.Length);
        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0)
                return true;
        }
        return false;
    }
}

public enum FileContentKind
{
    Image,
    Text,
    FilePath,
}

public sealed record FileContent(FileContentKind Kind, string Path);
