using Fling.Config;

namespace Fling.Tests;

public sealed class FlingLogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logPath;

    public FlingLogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logPath = Path.Combine(_tempDir, "fling.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Write_Disabled_DoesNotCreateFile()
    {
        var log = new FlingLog(false, _logPath);

        log.Write(["send", "--clipboard", "--all"], 0);

        Assert.False(File.Exists(_logPath));
    }

    [Fact]
    public void Write_Enabled_CreatesLogEntry()
    {
        var log = new FlingLog(true, _logPath);

        log.Write(["send", "--image", "test.png", "--all"], 0);

        var content = File.ReadAllText(_logPath);
        Assert.Contains("send --image test.png --all", content);
        Assert.Contains("exit=0", content);
    }

    [Fact]
    public void Write_WithDetail_IncludesDetail()
    {
        var log = new FlingLog(true, _logPath);

        log.Write(["send", "--clipboard", "--all"], 2, "connection refused");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("connection refused", content);
        Assert.Contains("exit=2", content);
    }

    [Fact]
    public void Write_MultipleInvocations_Appends()
    {
        var log = new FlingLog(true, _logPath);

        log.Write(["send", "--clipboard", "--all"], 0);
        log.Write(["status"], 0);

        var lines = File.ReadAllLines(_logPath);
        Assert.Equal(2, lines.Length);
    }
}
