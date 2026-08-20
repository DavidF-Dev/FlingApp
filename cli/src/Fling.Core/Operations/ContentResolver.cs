using System.Text;

namespace Fling.Content;

public sealed record ResolvedContent(string ContentType, byte[] Data);

/// <summary>
/// Turns a content source — clipboard, image file, arbitrary file, or literal text —
/// into bytes ready for encoding.
/// </summary>
public sealed class ContentResolver(IClipboardReader clipboard, IImageEncoder images)
{
    public ResolvedContent FromClipboard()
    {
        var content = clipboard.Read()
            ?? throw new ContentResolutionException("Clipboard is empty or contains unsupported content.");

        return new ResolvedContent(content.ContentType, content.Data);
    }

    public ResolvedContent FromImage(string path)
    {
        try
        {
            return new ResolvedContent("image/png", images.LoadAsPng(path));
        }
        catch (FileNotFoundException)
        {
            throw new ContentResolutionException($"Image file not found: {path}");
        }
        catch (Exception ex)
        {
            throw new ContentResolutionException($"Could not load image: {ex.Message}");
        }
    }

    public ResolvedContent FromFile(string path)
    {
        try
        {
            var file = FileContentResolver.Resolve(path);
            return file.Kind switch
            {
                FileContentKind.Image => new ResolvedContent("image/png", images.LoadAsPng(file.Path)),
                FileContentKind.Text => new ResolvedContent("text/plain", File.ReadAllBytes(file.Path)),
                _ => new ResolvedContent("text/plain", Encoding.UTF8.GetBytes(Path.GetFullPath(file.Path))),
            };
        }
        catch (FileNotFoundException)
        {
            throw new ContentResolutionException($"File not found: {path}");
        }
        catch (Exception ex)
        {
            throw new ContentResolutionException($"Could not read file: {ex.Message}");
        }
    }

    public static ResolvedContent FromText(string text) =>
        new("text/plain", Encoding.UTF8.GetBytes(text));
}

public sealed class ContentResolutionException(string message) : Exception(message);
