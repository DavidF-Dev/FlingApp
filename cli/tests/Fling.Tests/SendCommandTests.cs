using System.CommandLine;
using System.Net;
using System.Text;
using System.Text.Json;
using Fling.Commands;
using Fling.Config;
using Fling.Content;

namespace Fling.Tests;

public sealed class SendCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly ConfigStore _store;

    public SendCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _store = new ConfigStore(_configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SaveConfigWithDevice(string name = "Pixel 8", string host = "10.0.0.1")
    {
        _store.Save(new FlingConfig
        {
            Devices = [new DeviceConfig { Name = name, Host = host, ApiKey = "test-key" }],
        });
    }

    private int Invoke(IClipboardReader clipboard, params string[] args)
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(SendCommand.Create(_store, clipboard));
        return rootCommand.Parse(args).Invoke();
    }

    [Fact]
    public void NoContentSource_ReturnsError()
    {
        SaveConfigWithDevice();
        var exitCode = Invoke(new FakeClipboard(null), "send", "--device", "Pixel 8");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void MultipleContentSources_ReturnsError()
    {
        SaveConfigWithDevice();
        var exitCode = Invoke(new FakeClipboard(null), "send", "--clipboard", "--text", "hi", "--device", "Pixel 8");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void EmptyClipboard_ReturnsError()
    {
        SaveConfigWithDevice();
        var exitCode = Invoke(new FakeClipboard(null), "send", "--clipboard", "--device", "Pixel 8");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void DryRun_DoesNotSend()
    {
        SaveConfigWithDevice();
        var clipboard = new FakeClipboard(new ClipboardContent
        {
            ContentType = "text/plain",
            Data = Encoding.UTF8.GetBytes("hello"),
        });

        var exitCode = Invoke(clipboard, "send", "--clipboard", "--dry-run");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void DryRun_Text_PrintsDetails()
    {
        SaveConfigWithDevice();
        var output = new StringWriter();
        Console.SetOut(output);

        try
        {
            var clipboard = new FakeClipboard(new ClipboardContent
            {
                ContentType = "text/plain",
                Data = Encoding.UTF8.GetBytes("test content"),
            });

            Invoke(clipboard, "send", "--clipboard", "--dry-run");

            var text = output.ToString();
            Assert.Contains("text/plain", text);
            Assert.Contains("Dry run", text);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void NoDeviceSpecified_ReturnsError()
    {
        _store.Save(new FlingConfig
        {
            Devices =
            [
                new DeviceConfig { Name = "Phone A", Host = "10.0.0.1", ApiKey = "k1" },
                new DeviceConfig { Name = "Phone B", Host = "10.0.0.2", ApiKey = "k2" },
            ],
        });

        var clipboard = new FakeClipboard(new ClipboardContent
        {
            ContentType = "text/plain",
            Data = "hi"u8.ToArray(),
        });

        var exitCode = Invoke(clipboard, "send", "--clipboard");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void TextOption_EncodesAsTextPlain()
    {
        SaveConfigWithDevice();
        var output = new StringWriter();
        Console.SetOut(output);

        try
        {
            Invoke(new FakeClipboard(null), "send", "--text", "hello world", "--dry-run");

            var text = output.ToString();
            Assert.Contains("text/plain", text);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    private sealed class FakeClipboard : IClipboardReader
    {
        private readonly ClipboardContent? _content;
        public FakeClipboard(ClipboardContent? content) => _content = content;
        public ClipboardContent? Read() => _content;
    }
}
