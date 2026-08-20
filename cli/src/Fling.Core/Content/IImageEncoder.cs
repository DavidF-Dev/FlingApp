namespace Fling.Content;

/// <summary>
/// Loads an image file and returns it as PNG bytes.
/// </summary>
public interface IImageEncoder
{
    byte[] LoadAsPng(string filePath);
}
