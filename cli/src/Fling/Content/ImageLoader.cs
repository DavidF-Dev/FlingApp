using System.Drawing;
using System.Drawing.Imaging;

namespace Fling.Content;

public static class ImageLoader
{
    public static byte[] LoadAsPng(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Image file not found: {filePath}");

        using var image = Image.FromFile(filePath);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
