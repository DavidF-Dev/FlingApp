using System.IO.Compression;

namespace Fling.Content;

public sealed class ContentEncoder
{
    private readonly bool _compressEnabled;
    private readonly int _maxSizeBytes;

    public ContentEncoder(bool compressEnabled, int maxSizeMb)
    {
        _compressEnabled = compressEnabled;
        _maxSizeBytes = maxSizeMb * 1024 * 1024;
    }

    public ClipPayload Encode(string contentType, byte[] rawBytes)
    {
        if (rawBytes.Length > _maxSizeBytes)
            throw new ContentTooLargeException(rawBytes.Length, _maxSizeBytes);

        var shouldCompress = _compressEnabled && IsTextType(contentType);
        var dataBytes = shouldCompress ? GZipCompress(rawBytes) : rawBytes;

        return new ClipPayload
        {
            Type = contentType,
            Data = Convert.ToBase64String(dataBytes),
            Compressed = shouldCompress,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
    }

    private static bool IsTextType(string contentType) =>
        contentType is "text/plain" or "text/html";

    private static byte[] GZipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(data);
        }
        return output.ToArray();
    }
}

public sealed class ContentTooLargeException : Exception
{
    public ContentTooLargeException(long actualBytes, long maxBytes)
        : base($"Content size ({actualBytes / (1024.0 * 1024.0):F1} MB) exceeds maximum ({maxBytes / (1024.0 * 1024.0):F0} MB).")
    {
    }
}
