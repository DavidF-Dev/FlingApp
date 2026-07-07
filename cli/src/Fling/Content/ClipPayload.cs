namespace Fling.Content;

public sealed class ClipPayload
{
    public string Type { get; init; } = "";
    public string Data { get; init; } = "";
    public bool Compressed { get; init; }
    public long Timestamp { get; init; }
}
