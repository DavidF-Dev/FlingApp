using Fling.Operations;

namespace Fling.Gui.ViewModels;

/// <summary>
/// One device's send outcome, phrased for the results list.
/// </summary>
public sealed class SendResultViewModel(SendDeviceResult result)
{
    public string DeviceName => result.Device.Name;

    public bool Success => result.Success;

    public string Outcome => result.Success
        ? "received it."
        : result.AuthFailed
            ? "rejected the key — pair again from the Device manager."
            : $"could not be reached: {result.Error}";
}
