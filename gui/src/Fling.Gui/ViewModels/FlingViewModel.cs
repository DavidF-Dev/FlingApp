using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Fling.Config;
using Fling.Content;
using Fling.Gui.Settings;
using Fling.Operations;

namespace Fling.Gui.ViewModels;

/// <summary>
/// A device, or every device, to send to.
/// </summary>
public sealed record SendTarget(string Label, DeviceConfig? Device)
{
    public bool IsAll => Device is null;
}

/// <summary>
/// Drives the Fling window: what is staged, what it will look like on the other end,
/// where it is going, and how the send turned out.
/// </summary>
public sealed class FlingViewModel : ObservableObject
{
    private readonly ConfigStore _store;
    private readonly GuiSettingsStore _settingsStore;
    private readonly IClipboardReader _clipboard;
    private readonly IImageEncoder _images;
    private readonly SendOperation _send;

    private CancellationTokenSource? _sendCancellation;
    private StagedItem _staged = StagedItem.None;
    private string _editableText = "";
    private string? _statusMessage;
    private bool _isSending;
    private SendTarget? _selectedTarget;
    private IReadOnlyList<SendResultViewModel>? _results;

    public FlingViewModel(
        ConfigStore store,
        GuiSettingsStore settingsStore,
        IClipboardReader clipboard,
        IImageEncoder images,
        SendOperation send)
    {
        _store = store;
        _settingsStore = settingsStore;
        _clipboard = clipboard;
        _images = images;
        _send = send;

        LoadTargets(_settingsStore.Load());
    }

    public ObservableCollection<SendTarget> Targets { get; } = [];

    public bool HasTargets => Targets.Count > 0;

    public StagedKind Kind => _staged.Kind;

    public string SourceLabel => _staged.SourceLabel;

    public byte[] ImageBytes => _staged.ImageBytes;

    public bool IsText => Kind is StagedKind.Text or StagedKind.Html;

    public bool IsImage => Kind == StagedKind.Image;

    public bool ShowEmptyPreview => Kind is StagedKind.None or StagedKind.Rejected;

    public string EditableText
    {
        get => _editableText;
        set
        {
            if (Set(ref _editableText, value))
                RefreshPreview();
        }
    }

    public SendTarget? SelectedTarget
    {
        get => _selectedTarget;
        set => Set(ref _selectedTarget, value);
    }

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (Set(ref _isSending, value))
                Raise(nameof(CanSend));
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public string PreviewSummary { get; private set; } = "";

    public bool IsTooLarge { get; private set; }

    public IReadOnlyList<SendResultViewModel>? Results
    {
        get => _results;
        private set => Set(ref _results, value);
    }

    public bool CanSend => Kind is StagedKind.Text or StagedKind.Html or StagedKind.Image
                           && !IsSending
                           && !IsTooLarge
                           && HasTargets;

    // --- Staging ---------------------------------------------------------------------

    /// <summary>
    /// Stages whatever is on the clipboard.
    /// </summary>
    /// <param name="userInitiated">
    /// False when the window is simply opening. Content its owner marked as protected is
    /// skipped in that case — putting a password on screen because a window happened to
    /// open is not something the user asked for. An explicit paste stages it anyway.
    /// </param>
    public void StageFromClipboard(bool userInitiated)
    {
        var result = _clipboard.Read();

        if (result.Content is null)
        {
            Stage(StagedItem.None);
            StatusMessage = "Nothing to send yet. Copy something, drop a file here, or choose one.";
            return;
        }

        if (result.IsProtected && !userInitiated)
        {
            Stage(StagedItem.None);
            StatusMessage = "Your clipboard holds content an app asked to keep private — a password, most likely. "
                            + "Press Ctrl+V to send it anyway.";
            return;
        }

        Stage(StagedItem.FromClipboard(result.Content));
    }

    public void StageFromFile(string path) => Stage(StagedItem.FromFile(path, _images));

    /// <summary>
    /// Stages the first file of a drop. Fling sends one thing at a time, so a multi-file
    /// drop takes the first rather than silently discarding the rest.
    /// </summary>
    public void StageFromDrop(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;

        StageFromFile(paths[0]);

        if (paths.Count > 1)
            StatusMessage = $"Staged {Path.GetFileName(paths[0])}. Fling sends one item at a time.";
    }

    private void Stage(StagedItem item)
    {
        _staged = item;
        _editableText = item.Text;
        Results = null;
        StatusMessage = item.Kind == StagedKind.Rejected ? item.RejectionReason : null;

        RefreshPreview();

        Raise(nameof(Kind));
        Raise(nameof(SourceLabel));
        Raise(nameof(ImageBytes));
        Raise(nameof(IsText));
        Raise(nameof(IsImage));
        Raise(nameof(ShowEmptyPreview));
        Raise(nameof(EditableText));
    }

    private void RefreshPreview()
    {
        var config = _store.Load();
        var bytes = CurrentByteCount();
        var limit = (long)config.MaxSizeMb * 1024 * 1024;

        IsTooLarge = bytes > limit;

        PreviewSummary = Kind switch
        {
            StagedKind.Image => $"PNG · {_staged.ImageWidth} × {_staged.ImageHeight} · {FormatSize(bytes)}",
            StagedKind.Html => $"Rich text · {EditableText.Length:N0} characters · {FormatSize(bytes)}",
            StagedKind.Text => $"Plain text · {EditableText.Length:N0} characters · {FormatSize(bytes)}",
            _ => "",
        };

        if (IsTooLarge)
            StatusMessage = $"That is {FormatSize(bytes)}, over the {config.MaxSizeMb} MB limit. Raise it in Settings, or send something smaller.";

        Raise(nameof(PreviewSummary));
        Raise(nameof(IsTooLarge));
        Raise(nameof(CanSend));
    }

    private long CurrentByteCount() => Kind switch
    {
        StagedKind.Image => _staged.ImageBytes.Length,
        StagedKind.Text or StagedKind.Html => Encoding.UTF8.GetByteCount(EditableText),
        _ => 0,
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };

    // --- Targets ---------------------------------------------------------------------

    private void LoadTargets(GuiSettings settings)
    {
        Targets.Clear();

        var devices = _store.Load().Devices;

        // "All" only earns a place once there is more than one device to mean.
        if (devices.Count > 1)
            Targets.Add(new SendTarget("All devices", null));

        foreach (var device in devices)
            Targets.Add(new SendTarget(device.Name, device));

        SelectedTarget = ResolveInitialTarget(settings, devices.Count);

        Raise(nameof(HasTargets));
        Raise(nameof(CanSend));
    }

    private SendTarget? ResolveInitialTarget(GuiSettings settings, int deviceCount)
    {
        if (Targets.Count == 0)
            return null;

        if (settings.RememberLastDevice && settings.LastDevice.Length > 0)
        {
            var remembered = Targets.FirstOrDefault(t =>
                t.Device?.Name.Equals(settings.LastDevice, StringComparison.OrdinalIgnoreCase) == true);

            if (remembered is not null)
                return remembered;
        }

        // Targets[0] is "All devices" when there is more than one, otherwise the only one.
        return Targets[0];
    }

    // --- Sending ---------------------------------------------------------------------

    /// <summary>
    /// Sends the staged content. Returns whether every target accepted it.
    /// </summary>
    public async Task<bool> SendAsync(CancellationToken ct = default)
    {
        if (!CanSend || SelectedTarget is null)
            return false;

        var config = _store.Load();
        var devices = SelectedTarget.IsAll
            ? config.Devices
            : config.Devices.Where(d => d.Name.Equals(SelectedTarget.Device!.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (devices.Count == 0)
        {
            StatusMessage = "That device is no longer paired.";
            return false;
        }

        ClipPayload payload;
        try
        {
            payload = SendOperation.Encode(config, BuildContent());
        }
        catch (ContentTooLargeException ex)
        {
            StatusMessage = ex.Message;
            return false;
        }

        _sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsSending = true;
        StatusMessage = devices.Count == 1 ? $"Sending to {devices[0].Name}…" : $"Sending to {devices.Count} devices…";

        IReadOnlyList<SendDeviceResult> results;
        try
        {
            results = await _send.SendAsync(config, devices, payload, ct: _sendCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Send cancelled.";
            return false;
        }
        finally
        {
            IsSending = false;
            _sendCancellation.Dispose();
            _sendCancellation = null;
        }

        Results = results.Select(r => new SendResultViewModel(r)).ToList();
        RememberTarget();

        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count == 0)
        {
            StatusMessage = null;
            return true;
        }

        StatusMessage = failures.Count == results.Count && results.Count == 1
            ? $"Could not send to {failures[0].Device.Name}: {failures[0].Error}"
            : $"Sent to {results.Count - failures.Count} of {results.Count} devices.";

        return false;
    }

    public void CancelSend() => _sendCancellation?.Cancel();

    private ResolvedContent BuildContent() => Kind switch
    {
        StagedKind.Image => new ResolvedContent("image/png", _staged.ImageBytes),
        StagedKind.Html => new ResolvedContent("text/html", Encoding.UTF8.GetBytes(EditableText)),
        _ => new ResolvedContent("text/plain", Encoding.UTF8.GetBytes(EditableText)),
    };

    private void RememberTarget()
    {
        if (SelectedTarget?.Device is null)
            return;

        _settingsStore.Update(s => s.LastDevice = SelectedTarget.Device.Name);
    }
}
