using System.Windows.Forms;
using Fling.Gui.Settings;
using Fling.Operations;

namespace Fling.Gui.Tray;

/// <summary>
/// Announces the outcome of a send once its window has gone.
/// </summary>
public interface ISendNotifier
{
    void SendCompleted(IReadOnlyList<SendDeviceResult> results);
}

/// <summary>
/// What a send outcome should produce, given the user's preference.
/// </summary>
public enum NotificationAction
{
    Nothing,
    FlashOnly,
    ShowSuccess,
    ShowFailure,
}

/// <summary>
/// Reports send outcomes through the notification area.
/// </summary>
/// <remarks>
/// Uses balloon tips rather than the Windows toast APIs, which require an application
/// identity and a Start menu shortcut before they will render for an unpackaged app.
/// Windows presents a balloon as a toast anyway.
/// </remarks>
public sealed class SendNotifier(TrayIconHost tray, GuiSettingsStore settings) : ISendNotifier
{
    private const int BalloonTextLimit = 255;
    private const int MaxNamesListed = 3;
    private const int MaxNameLength = 30;


    public void SendCompleted(IReadOnlyList<SendDeviceResult> results)
    {
        if (results.Count == 0)
            return;

        var failures = results.Where(r => !r.Success).ToList();

        switch (Decide(settings.Load().Notifications, failures.Count))
        {
            case NotificationAction.FlashOnly:
                tray.FlashSuccess();
                break;

            case NotificationAction.ShowSuccess:
                tray.FlashSuccess();
                tray.ShowBalloon("Sent", DescribeSuccess(results), ToolTipIcon.Info);
                break;

            case NotificationAction.ShowFailure:
                tray.ShowBalloon(FailureTitle(failures, results.Count), DescribeFailure(failures), ToolTipIcon.Warning);
                break;
        }
    }

    /// <summary>
    /// Failures are worth interrupting for; success is not, which is why the default
    /// setting marks the icon instead of raising a notification per send.
    /// </summary>
    internal static NotificationAction Decide(NotificationMode mode, int failureCount) => mode switch
    {
        NotificationMode.Never => NotificationAction.Nothing,
        NotificationMode.Always when failureCount == 0 => NotificationAction.ShowSuccess,
        _ when failureCount == 0 => NotificationAction.FlashOnly,
        _ => NotificationAction.ShowFailure,
    };

    internal static string DescribeSuccess(IReadOnlyList<SendDeviceResult> results) =>
        results.Count == 1
            ? $"{results[0].Device.Name} received it."
            : $"All {results.Count} devices received it.";

    internal static string FailureTitle(IReadOnlyList<SendDeviceResult> failures, int total) =>
        failures.Count == total ? "Fling could not send" : "Fling sent to some devices";

    /// <summary>
    /// Names the device and separates a rejected key from an unreachable device, because
    /// the two need different things from the user.
    /// </summary>
    /// <remarks>
    /// Kept inside the balloon's limit by construction. Windows truncates a longer
    /// message silently, and it is the tail — the device name, or what went wrong — that
    /// would be lost.
    /// </remarks>
    internal static string DescribeFailure(IReadOnlyList<SendDeviceResult> failures)
    {
        if (failures.Count == 1)
        {
            var failure = failures[0];
            var name = Truncate(failure.Device.Name, MaxNameLength);

            if (failure.AuthFailed)
                return $"{name} rejected the key. Pair it again from the Device manager.";

            var prefix = $"{name} could not be reached. ";
            return prefix + Truncate(failure.Error ?? "", BalloonTextLimit - prefix.Length);
        }

        if (failures.All(f => f.AuthFailed))
            return $"{JoinNames(failures)} rejected the key. Pair them again from the Device manager.";

        return $"Could not reach {JoinNames(failures)}.";
    }

    /// <summary>
    /// Lists the first few names and counts the rest, so a send to many devices does not
    /// bury the message under a list of them.
    /// </summary>
    private static string JoinNames(IReadOnlyList<SendDeviceResult> failures)
    {
        var listed = failures.Take(MaxNamesListed).Select(f => Truncate(f.Device.Name, MaxNameLength));
        var joined = string.Join(", ", listed);
        var remaining = failures.Count - MaxNamesListed;

        return remaining > 0 ? $"{joined} and {remaining} more" : joined;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";
}
