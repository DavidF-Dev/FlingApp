using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fling.Config;
using Fling.Content;
using Fling.Gui.Settings;
using Fling.Gui.ViewModels;
using Fling.Operations;

namespace Fling.Gui.Windows;

public partial class FlingWindow : Window
{
    private readonly FlingViewModel _model;
    private readonly CancellationTokenSource _lifetime = new();

    // Captured before this window exists, while the foreground window is still whatever
    // the user was working in.
    private readonly IntPtr _activeWindow = WindowPlacement.CaptureActiveWindow();

    public FlingWindow(FlingViewModel model)
    {
        InitializeComponent();

        _model = model;
        _model.PropertyChanged += OnModelPropertyChanged;
        DataContext = _model;

        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowPlacement.CenterOnActiveScreen(this, _activeWindow);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Staging on open collapses the common case — copy, then send — to a single key.
        _model.StageFromClipboard(userInitiated: false);
        UpdateImagePreview();

        if (_model.IsText)
            TextPreview.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _model.CancelSend();
        _lifetime.Cancel();
        base.OnClosing(e);
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlingViewModel.ImageBytes) or nameof(FlingViewModel.IsImage))
            UpdateImagePreview();
    }

    private void UpdateImagePreview()
    {
        if (!_model.IsImage || _model.ImageBytes.Length == 0)
        {
            ImagePreview.Source = null;
            return;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(_model.ImageBytes);
        // Without this the stream must outlive the image, and it is discarded here.
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        ImagePreview.Source = bitmap;
    }

    // --- Input -----------------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                return;

            // The preview is an editable text box, so Ctrl+V would otherwise paste into
            // it rather than restage from the clipboard.
            case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                _model.StageFromClipboard(userInitiated: true);
                UpdateImagePreview();
                return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        e.Handled = true;
        _model.StageFromDrop(paths);
        UpdateImagePreview();
    }

    private void OnChooseFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose something to fling",
            Filter = "Images and text|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.txt;*.md;*.csv;*.json;*.xml"
                     + "|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
                     + "|Text|*.txt;*.md;*.csv;*.json;*.xml"
                     + "|All files|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _model.StageFromFile(dialog.FileName);
        UpdateImagePreview();
    }

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        var succeeded = await _model.SendAsync(_lifetime.Token);

        // Failures stay on screen with their per-device detail; a clean send has nothing
        // left to say.
        if (succeeded)
            Close();
    }

    private void OnCancelSendClicked(object sender, RoutedEventArgs e) => _model.CancelSend();
}
