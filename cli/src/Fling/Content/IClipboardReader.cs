namespace Fling.Content;

public interface IClipboardReader
{
    ClipboardContent? Read();
}

public sealed class ClipboardContent
{
    public required string ContentType { get; init; }
    public required byte[] Data { get; init; }
}
