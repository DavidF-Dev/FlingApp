using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Fling.Config;
using Fling.Gui.ViewModels;
using Fling.Net;
using Fling.Operations;

namespace Fling.Gui.Windows;

public partial class DeviceManagerWindow : Window
{
    private static readonly TimeSpan ReachabilityInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(4);

    private readonly DeviceManagerViewModel _model;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _reachabilityTimer;
    private readonly DispatcherTimer _discoveryTimer;

    public DeviceManagerWindow()
        : this(BuildDefaultModel())
    {
    }

    public DeviceManagerWindow(DeviceManagerViewModel model)
    {
        InitializeComponent();

        _model = model;
        _model.PropertyChanged += OnModelPropertyChanged;
        DataContext = _model;

        _reachabilityTimer = new DispatcherTimer { Interval = ReachabilityInterval };
        _reachabilityTimer.Tick += async (_, _) => await _model.RefreshReachabilityAsync(_lifetime.Token);

        _discoveryTimer = new DispatcherTimer { Interval = DiscoveryInterval };
        _discoveryTimer.Tick += async (_, _) => await _model.PollDiscoveryAsync(_lifetime.Token);

        Loaded += OnLoaded;
    }

    private static DeviceManagerViewModel BuildDefaultModel()
    {
        var store = new ConfigStore();
        return new DeviceManagerViewModel(
            store,
            new ReachabilityProbe(store),
            new UdpDiscovery(),
            new PairOperation(store));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _reachabilityTimer.Start();
        _discoveryTimer.Start();

        // Kick both off immediately; the timers only cover subsequent rounds.
        var reachability = _model.RefreshReachabilityAsync(_lifetime.Token);
        var discovery = _model.PollDiscoveryAsync(_lifetime.Token);
        await Task.WhenAll(reachability, discovery);
    }

    /// <summary>
    /// Stops all polling and abandons any pairing still in flight, so nothing outlives
    /// the window or writes to the config after it is gone.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        _reachabilityTimer.Stop();
        _discoveryTimer.Stop();
        _model.CancelPairing();
        _lifetime.Cancel();

        base.OnClosing(e);
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceManagerViewModel.PairingState)
            or nameof(DeviceManagerViewModel.PairingMessage))
        {
            UpdatePairingOverlay();
        }
    }

    private void UpdatePairingOverlay()
    {
        switch (_model.PairingState)
        {
            case PairingState.Idle:
                PairingOverlay.Visibility = Visibility.Collapsed;
                return;

            case PairingState.WaitingForApproval:
                PairingTitle.Text = $"Pairing with {_model.PairingDeviceName}";
                PairingDetail.Text = _model.PairingMessage;
                PairingCancel.Visibility = Visibility.Visible;
                PairingDismiss.Visibility = Visibility.Collapsed;
                break;

            case PairingState.Accepted:
                PairingTitle.Text = "Paired";
                PairingDetail.Text = _model.PairingMessage;
                ShowDismissOnly();
                break;

            case PairingState.Rejected:
                PairingTitle.Text = "Pairing declined";
                PairingDetail.Text = "The request was declined on the device.";
                ShowDismissOnly();
                break;

            case PairingState.TimedOut:
                PairingTitle.Text = "No response";
                PairingDetail.Text = _model.PairingMessage;
                ShowDismissOnly();
                break;

            case PairingState.Cancelled:
                PairingOverlay.Visibility = Visibility.Collapsed;
                _model.ClearPairingState();
                return;

            default:
                PairingTitle.Text = "Pairing failed";
                PairingDetail.Text = _model.PairingMessage;
                ShowDismissOnly();
                break;
        }

        PairingOverlay.Visibility = Visibility.Visible;
    }

    private void ShowDismissOnly()
    {
        PairingCancel.Visibility = Visibility.Collapsed;
        PairingDismiss.Visibility = Visibility.Visible;
    }

    private async void OnPairClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DiscoveredDeviceViewModel device)
            await _model.PairAsync(device, _lifetime.Token);
    }

    private async void OnManualPairClicked(object sender, RoutedEventArgs e) => await PairManually();

    private async void OnManualEndpointKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await PairManually();
    }

    private async Task PairManually()
    {
        var endpoint = ManualEndpoint.Text.Trim();
        if (endpoint.Length == 0)
            return;

        await _model.PairManualAsync(endpoint, _lifetime.Token);

        if (_model.PairingState == PairingState.Accepted)
            ManualEndpoint.Clear();
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not PairedDeviceViewModel device)
            return;

        var confirmed = MessageBox.Show(
            $"Remove '{device.Name}'?\n\n"
            + "Fling will no longer be able to send to it until you pair again. "
            + "The device keeps its own record of this PC until you clear it there.",
            "Remove device",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (confirmed == MessageBoxResult.OK)
            _model.RemoveDevice(device);
    }

    private void OnCancelPairingClicked(object sender, RoutedEventArgs e) => _model.CancelPairing();

    private void OnDismissPairingClicked(object sender, RoutedEventArgs e) => _model.ClearPairingState();
}
