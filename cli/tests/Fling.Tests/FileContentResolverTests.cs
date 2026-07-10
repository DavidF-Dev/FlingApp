using Fling.Content;

namespace Fling.Tests;

public sealed class FileContentResolverTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"fling-test-{Guid.NewGuid():N}");

    public FileContentResolverTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".bmp")]
    [InlineData(".gif")]
    [InlineData(".PNG")]
    [InlineData(".Jpg")]
    public void ImageExtension_ReturnsImage(string ext)
    {
        var path = CreateFile($"test{ext}", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var result = FileContentResolver.Resolve(path);
        Assert.Equal(FileContentKind.Image, result.Kind);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".json")]
    [InlineData(".xml")]
    [InlineData(".csv")]
    [InlineData(".md")]
    [InlineData(".log")]
    public void TextFile_ReturnsText(string ext)
    {
        var path = CreateFile($"test{ext}", "Hello, world!"u8.ToArray());
        var result = FileContentResolver.Resolve(path);
        Assert.Equal(FileContentKind.Text, result.Kind);
    }

    [Fact]
    public void BinaryFile_ReturnsFilePath()
    {
        var data = new byte[100];
        data[50] = 0x00;
        data[0] = 0xFF;
        data[1] = 0xFE;
        var path = CreateFile("test.dat", data);
        var result = FileContentResolver.Resolve(path);
        Assert.Equal(FileContentKind.FilePath, result.Kind);
    }

    [Fact]
    public void NullByteInSample_DetectedAsBinary()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x00, 0x6F };
        var path = CreateFile("test.dat", data);
        var result = FileContentResolver.Resolve(path);
        Assert.Equal(FileContentKind.FilePath, result.Kind);
    }

    [Fact]
    public void NoNullBytes_DetectedAsText()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var path = CreateFile("test.dat", data);
        var result = FileContentResolver.Resolve(path);
        Assert.Equal(FileContentKind.Text, result.Kind);
    }

    [Fact]
    public void MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            FileContentResolver.Resolve(Path.Combine(_tempDir, "missing.txt")));
    }

    [Fact]
    public void EmptyFile_Throws()
    {
        var path = CreateFile("empty.txt", Array.Empty<byte>());
        Assert.Throws<InvalidOperationException>(() =>
            FileContentResolver.Resolve(path));
    }

    [Fact]
    public void UnknownExtension_WithTextContent_ReturnsText()
    {
        var path = CreateFile("test.cfg", "key=value\n"u8.ToArray());
        var result = FileContentResolver.Resolve(path);
        Assert.Equal(FileContentKind.Text, result.Kind);
    }

    private string CreateFile(string name, byte[] content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { }
    }
}
