using System.Drawing;
using System.Drawing.Imaging;
using Fling.Content;

namespace Fling.Tests;

public sealed class PngPassThroughTests : IDisposable
{
    private readonly string _tempDir;

    public PngPassThroughTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadAsPng_ValidPng_ReturnsFileBytesUnchanged()
    {
        var path = CreateImage(ImageFormat.Png, "shot.png");
        var onDisk = File.ReadAllBytes(path);

        var result = ImageLoader.LoadAsPng(path);

        Assert.Equal(onDisk, result);
    }

    [Fact]
    public void LoadAsPng_JpegNamedAsPng_IsReEncodedNotPassedThrough()
    {
        var path = Path.Combine(_tempDir, "actually-jpeg.png");
        using (var bmp = new Bitmap(10, 10))
            bmp.Save(path, ImageFormat.Jpeg);

        var onDisk = File.ReadAllBytes(path);
        var result = ImageLoader.LoadAsPng(path);

        Assert.NotEqual(onDisk, result);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], result[..4]);
    }

    [Fact]
    public void LoadAsPng_TruncatedPng_Throws()
    {
        var path = CreateImage(ImageFormat.Png, "truncated.png");
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        Assert.ThrowsAny<Exception>(() => ImageLoader.LoadAsPng(path));
    }

    [Fact]
    public void LoadAsPng_FileShorterThanSignature_ThrowsFromDecoder()
    {
        var path = Path.Combine(_tempDir, "tiny.png");
        File.WriteAllBytes(path, [0x89, 0x50]);

        var ex = Record.Exception(() => ImageLoader.LoadAsPng(path));

        Assert.NotNull(ex);
        Assert.IsNotType<IndexOutOfRangeException>(ex);
        Assert.IsNotType<ArgumentOutOfRangeException>(ex);
    }

    [Fact]
    public void LoadAsPng_EmptyFile_DoesNotThrowIndexingError()
    {
        var path = Path.Combine(_tempDir, "empty.png");
        File.WriteAllBytes(path, []);

        var ex = Record.Exception(() => ImageLoader.LoadAsPng(path));

        Assert.NotNull(ex);
        Assert.IsNotType<IndexOutOfRangeException>(ex);
    }

    private string CreateImage(ImageFormat format, string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        using var bmp = new Bitmap(10, 10);
        using (var graphics = Graphics.FromImage(bmp))
            graphics.Clear(Color.CornflowerBlue);
        bmp.Save(path, format);
        return path;
    }
}
