namespace Fling.Content;

public interface IClipboardReader
{
    ClipboardReadResult Read();
}

public sealed class ClipboardContent
{
    public required string ContentType { get; init; }
    public required byte[] Data { get; init; }
}

/// <summary>
/// What the clipboard held, and whether its owner asked for it not to be captured.
/// </summary>
/// <param name="Content">Null when the clipboard is empty or holds nothing Fling can send.</param>
/// <param name="IsProtected">
/// True when the content carries the formats password managers set to opt out of
/// clipboard history and monitoring. The content is still returned: refusing an explicit
/// send would override a choice the user just made. It is the caller's business whether
/// to surface such content unprompted.
/// </param>
public sealed record ClipboardReadResult(ClipboardContent? Content, bool IsProtected = false)
{
    public static readonly ClipboardReadResult Empty = new(Content: null);

    public bool HasContent => Content is not null;
}
