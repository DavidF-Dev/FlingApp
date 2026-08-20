using System.IO;
using System.Text;
using Fling.Content;

namespace Fling.Gui.ViewModels;

public enum StagedKind
{
    None,
    Text,
    Html,
    Image,
    Rejected,
}

/// <summary>
/// The one thing currently queued to send, resolved to bytes Fling can transmit.
/// </summary>
public sealed record StagedItem
{
    public required StagedKind Kind { get; init; }
    public string SourceLabel { get; init; } = "";
    public string Text { get; init; } = "";
    public byte[] ImageBytes { get; init; } = [];
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public string RejectionReason { get; init; } = "";

    public static readonly StagedItem None = new() { Kind = StagedKind.None };

    public static StagedItem Rejected(string sourceLabel, string reason) =>
        new() { Kind = StagedKind.Rejected, SourceLabel = sourceLabel, RejectionReason = reason };

    public static StagedItem FromImage(byte[] png, string sourceLabel)
    {
        PngInfo.TryGetDimensions(png, out var width, out var height);
        return new StagedItem
        {
            Kind = StagedKind.Image,
            SourceLabel = sourceLabel,
            ImageBytes = png,
            ImageWidth = width,
            ImageHeight = height,
        };
    }

    public static StagedItem FromClipboard(ClipboardContent content) => content.ContentType switch
    {
        "image/png" => FromImage(content.Data, "Clipboard"),
        "text/html" => new StagedItem
        {
            Kind = StagedKind.Html,
            SourceLabel = "Clipboard",
            Text = Encoding.UTF8.GetString(content.Data),
        },
        _ => new StagedItem
        {
            Kind = StagedKind.Text,
            SourceLabel = "Clipboard",
            Text = Encoding.UTF8.GetString(content.Data),
        },
    };

    /// <summary>
    /// Resolves a file, rejecting anything the phone could not paste.
    /// </summary>
    /// <remarks>
    /// The CLI falls back to sending such a file's path as text, which backstops Explorer
    /// "Send to" where the alternative is nothing at all. Behind a preview and a Fling
    /// button it would read as file transfer and deliver a useless string, so the window
    /// refuses instead.
    /// </remarks>
    public static StagedItem FromFile(string path, IImageEncoder images)
    {
        var name = Path.GetFileName(path);

        try
        {
            var resolved = FileContentResolver.Resolve(path);
            return resolved.Kind switch
            {
                FileContentKind.Image => FromImage(images.LoadAsPng(resolved.Path), name),
                FileContentKind.Text => new StagedItem
                {
                    Kind = StagedKind.Text,
                    SourceLabel = name,
                    Text = File.ReadAllText(resolved.Path),
                },
                _ => Rejected(name, $"Fling sends things you can paste, and {name} isn't one of them."),
            };
        }
        catch (FileNotFoundException)
        {
            return Rejected(name, $"{name} could not be found.");
        }
        catch (Exception ex)
        {
            return Rejected(name, $"{name} could not be read: {ex.Message}");
        }
    }
}
