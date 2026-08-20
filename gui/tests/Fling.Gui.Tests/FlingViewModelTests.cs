using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Text;
using Fling.Config;
using Fling.Content;
using Fling.Gui.Settings;
using Fling.Gui.ViewModels;
using Fling.Net;
using Fling.Operations;

namespace Fling.Gui.Tests;

public sealed class FlingViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigStore _store;
    private readonly GuiSettingsStore _settings;

    public FlingViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fling-gui-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new ConfigStore(Path.Combine(_tempDir, "config.json"));
        _settings = new GuiSettingsStore(Path.Combine(_tempDir, "gui.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FlingViewModel Build(ClipboardReadResult? clipboard = null, HttpMessageHandler? handler = null)
    {
        return new FlingViewModel(
            _store,
            _settings,
            new FakeClipboard(clipboard ?? ClipboardReadResult.Empty),
            new GdiImageEncoder(),
            new SendOperation(_store, () => new FlingHttpClient(handler ?? new OkHandler())));
    }

    private void SaveDevices(params string[] names) =>
        _store.Update(c =>
        {
            foreach (var (name, index) in names.Select((n, i) => (n, i)))
                c.Devices.Add(new DeviceConfig { Name = name, Host = $"10.0.0.{index + 1}", ApiKey = "key" });
        });

    private static ClipboardReadResult Text(string value, bool isProtected = false) =>
        new(new ClipboardContent { ContentType = "text/plain", Data = Encoding.UTF8.GetBytes(value) }, isProtected);

    private static ClipboardReadResult Html(string value) =>
        new(new ClipboardContent { ContentType = "text/html", Data = Encoding.UTF8.GetBytes(value) });

    private static ClipboardReadResult Image(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new ClipboardReadResult(new ClipboardContent { ContentType = "image/png", Data = stream.ToArray() });
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // --- Staging from the clipboard --------------------------------------------------

    [Fact]
    public void StageFromClipboard_PlainText_StagesEditableText()
    {
        var model = Build(Text("hello world"));

        model.StageFromClipboard(userInitiated: false);

        Assert.Equal(StagedKind.Text, model.Kind);
        Assert.Equal("hello world", model.EditableText);
        Assert.True(model.IsText);
    }

    [Fact]
    public void StageFromClipboard_HtmlOnly_IsStagedAsHtml()
    {
        var model = Build(Html("<b>rich</b>"));

        model.StageFromClipboard(userInitiated: false);

        Assert.Equal(StagedKind.Html, model.Kind);
    }

    [Fact]
    public void StageFromClipboard_Image_ReportsDimensions()
    {
        var model = Build(Image(320, 240));

        model.StageFromClipboard(userInitiated: false);

        Assert.Equal(StagedKind.Image, model.Kind);
        Assert.Contains("320 × 240", model.PreviewSummary);
    }

    [Fact]
    public void StageFromClipboard_Empty_ExplainsAndStagesNothing()
    {
        var model = Build();

        model.StageFromClipboard(userInitiated: false);

        Assert.Equal(StagedKind.None, model.Kind);
        Assert.False(model.CanSend);
        Assert.NotNull(model.StatusMessage);
    }

    // --- Protected content -----------------------------------------------------------

    [Fact]
    public void StageFromClipboard_ProtectedOnOpen_IsNotStaged()
    {
        SaveDevices("Pixel");
        var model = Build(Text("hunter2", isProtected: true));

        model.StageFromClipboard(userInitiated: false);

        Assert.Equal(StagedKind.None, model.Kind);
        Assert.DoesNotContain("hunter2", model.EditableText);
        Assert.Contains("keep private", model.StatusMessage);
    }

    [Fact]
    public void StageFromClipboard_ProtectedButUserAsked_IsStaged()
    {
        SaveDevices("Pixel");
        var model = Build(Text("hunter2", isProtected: true));

        model.StageFromClipboard(userInitiated: true);

        Assert.Equal(StagedKind.Text, model.Kind);
        Assert.Equal("hunter2", model.EditableText);
        Assert.True(model.CanSend);
    }

    // --- Staging from files ----------------------------------------------------------

    [Fact]
    public void StageFromFile_TextFile_StagesItsContent()
    {
        var model = Build();

        model.StageFromFile(WriteFile("notes.txt", "from a file"));

        Assert.Equal(StagedKind.Text, model.Kind);
        Assert.Equal("from a file", model.EditableText);
        Assert.Equal("notes.txt", model.SourceLabel);
    }

    [Fact]
    public void StageFromFile_BinaryFile_IsRejectedNotSentAsAPath()
    {
        var path = Path.Combine(_tempDir, "document.pdf");
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46, 0x00, 0x01, 0x02, 0x00]);
        var model = Build();

        model.StageFromFile(path);

        Assert.Equal(StagedKind.Rejected, model.Kind);
        Assert.False(model.CanSend);
        Assert.Contains("document.pdf", model.StatusMessage);
        Assert.DoesNotContain(_tempDir, model.EditableText);
    }

    [Fact]
    public void StageFromFile_MissingFile_IsRejected()
    {
        var model = Build();

        model.StageFromFile(Path.Combine(_tempDir, "nope.txt"));

        Assert.Equal(StagedKind.Rejected, model.Kind);
        Assert.False(model.CanSend);
    }

    [Fact]
    public void StageFromFile_ReplacesRatherThanAccumulates()
    {
        var model = Build(Text("from clipboard"));
        model.StageFromClipboard(userInitiated: false);

        model.StageFromFile(WriteFile("notes.txt", "from a file"));

        Assert.Equal("from a file", model.EditableText);
        Assert.Equal("notes.txt", model.SourceLabel);
    }

    [Fact]
    public void StageFromDrop_MultipleFiles_TakesTheFirstAndSaysSo()
    {
        var model = Build();
        var first = WriteFile("first.txt", "one");
        var second = WriteFile("second.txt", "two");

        model.StageFromDrop([first, second]);

        Assert.Equal("one", model.EditableText);
        Assert.Contains("one item at a time", model.StatusMessage);
    }

    // --- Size limit ------------------------------------------------------------------

    [Fact]
    public void EditableText_BeyondTheSizeLimit_BlocksSending()
    {
        SaveDevices("Pixel");
        _store.Update(c => c.MaxSizeMb = 1);
        var model = Build(Text("small"));
        model.StageFromClipboard(userInitiated: false);
        Assert.True(model.CanSend);

        model.EditableText = new string('x', 2 * 1024 * 1024);

        Assert.True(model.IsTooLarge);
        Assert.False(model.CanSend);
        Assert.Contains("over the 1 MB limit", model.StatusMessage);
    }

    // --- Targets ---------------------------------------------------------------------

    [Fact]
    public void Targets_SingleDevice_OffersOnlyThatDevice()
    {
        SaveDevices("Pixel");
        var model = Build();

        Assert.Single(model.Targets);
        Assert.Equal("Pixel", model.SelectedTarget!.Label);
    }

    [Fact]
    public void Targets_SeveralDevices_DefaultToAll()
    {
        SaveDevices("Pixel", "Tablet");
        var model = Build();

        Assert.Equal(3, model.Targets.Count);
        Assert.True(model.SelectedTarget!.IsAll);
    }

    [Fact]
    public void Targets_NoDevices_BlocksSending()
    {
        var model = Build(Text("hello"));
        model.StageFromClipboard(userInitiated: false);

        Assert.False(model.HasTargets);
        Assert.False(model.CanSend);
    }

    [Fact]
    public void Targets_RememberedDevice_IsPreselected()
    {
        SaveDevices("Pixel", "Tablet");
        _settings.Update(s => s.LastDevice = "Tablet");

        var model = Build();

        Assert.Equal("Tablet", model.SelectedTarget!.Label);
    }

    // --- Sending ---------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_AllDevicesSucceed_ReportsSuccess()
    {
        SaveDevices("Pixel", "Tablet");
        var model = Build(Text("hello"));
        model.StageFromClipboard(userInitiated: false);

        var succeeded = await model.SendAsync();

        Assert.True(succeeded);
        Assert.Equal(2, model.Results!.Count);
        Assert.All(model.Results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task SendAsync_OneDeviceFails_ReportsPartialSuccessPerDevice()
    {
        SaveDevices("Pixel", "Tablet");
        var model = Build(Text("hello"), new SelectiveHandler("10.0.0.2"));
        model.StageFromClipboard(userInitiated: false);

        var succeeded = await model.SendAsync();

        Assert.False(succeeded);
        Assert.Contains(model.Results!, r => r.Success);
        Assert.Contains(model.Results!, r => !r.Success);
        Assert.Contains("1 of 2", model.StatusMessage);
    }

    [Fact]
    public async Task SendAsync_AuthFailure_SaysToPairAgain()
    {
        SaveDevices("Pixel");
        var model = Build(Text("hello"), new StatusHandler(HttpStatusCode.Unauthorized));
        model.StageFromClipboard(userInitiated: false);

        await model.SendAsync();

        Assert.Contains("pair again", model.Results!.Single().Outcome);
    }

    [Fact]
    public async Task SendAsync_HtmlOnlyContent_SendsHtmlType()
    {
        SaveDevices("Pixel");
        var capture = new CapturingHandler();
        var model = Build(Html("<b>rich</b>"), capture);
        model.StageFromClipboard(userInitiated: false);

        await model.SendAsync();

        Assert.Contains("text/html", capture.LastBody);
    }

    [Fact]
    public async Task SendAsync_RemembersTheChosenDevice()
    {
        SaveDevices("Pixel", "Tablet");
        var model = Build(Text("hello"));
        model.StageFromClipboard(userInitiated: false);
        model.SelectedTarget = model.Targets.First(t => t.Label == "Tablet");

        await model.SendAsync();

        Assert.Equal("Tablet", _settings.Load().LastDevice);
    }

    [Fact]
    public async Task SendAsync_NothingStaged_DoesNothing()
    {
        SaveDevices("Pixel");
        var model = Build();

        Assert.False(await model.SendAsync());
        Assert.Null(model.Results);
    }

    // --- Fakes -----------------------------------------------------------------------

    private sealed class FakeClipboard(ClipboardReadResult result) : IClipboardReader
    {
        public ClipboardReadResult Read() => result;
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>
    /// Succeeds for every host except the one named, so a partial failure can be tested.
    /// </summary>
    private sealed class SelectiveHandler(string failingHost) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == failingHost)
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("unreachable"));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
