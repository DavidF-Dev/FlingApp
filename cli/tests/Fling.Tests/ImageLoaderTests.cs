using System.Drawing;
using System.Drawing.Imaging;
using Fling.Content;

namespace Fling.Tests;

public sealed class ImageLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ImageLoaderTests()
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
    public void LoadAsPng_ValidPng_ReturnsPngBytes()
    {
        var path = CreateTestImage(ImageFormat.Png, "test.png");

        var result = ImageLoader.LoadAsPng(path);

        // PNG magic bytes
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]); // P
        Assert.Equal(0x4E, result[2]); // N
        Assert.Equal(0x47, result[3]); // G
    }

    [Fact]
    public void LoadAsPng_ValidBmp_ConvertsToPng()
    {
        var path = CreateTestImage(ImageFormat.Bmp, "test.bmp");

        var result = ImageLoader.LoadAsPng(path);

        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
    }

    [Fact]
    public void LoadAsPng_ValidJpeg_ConvertsToPng()
    {
        var path = CreateTestImage(ImageFormat.Jpeg, "test.jpg");

        var result = ImageLoader.LoadAsPng(path);

        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
    }

    [Fact]
    public void LoadAsPng_NonExistentFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ImageLoader.LoadAsPng(Path.Combine(_tempDir, "nope.png")));
    }

    [Fact]
    public void LoadAsPng_NotAnImage_Throws()
    {
        var path = Path.Combine(_tempDir, "not-image.txt");
        File.WriteAllText(path, "this is not an image");

        Assert.ThrowsAny<Exception>(() => ImageLoader.LoadAsPng(path));
    }

    private string CreateTestImage(ImageFormat format, string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        using var bmp = new Bitmap(10, 10);
        bmp.Save(path, format);
        return path;
    }
}
