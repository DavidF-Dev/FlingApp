using Fling.Config;
using Fling.Gui.Settings;
using Fling.Gui.Tray;
using Fling.Operations;

namespace Fling.Gui.Tests;

public sealed class SendNotifierTests
{
    private static SendDeviceResult Ok(string name) =>
        new(Device(name), Success: true, Error: null, AuthFailed: false);

    private static SendDeviceResult Unreachable(string name, string error = "Connection timed out.") =>
        new(Device(name), Success: false, Error: error, AuthFailed: false);

    private static SendDeviceResult Unauthorized(string name) =>
        new(Device(name), Success: false, Error: "Authentication failed.", AuthFailed: true);

    private static DeviceConfig Device(string name) =>
        new() { Name = name, Host = "10.0.0.1", ApiKey = "key" };

    // --- What each preference produces -----------------------------------------------

    /// <summary>
    /// A notification per successful send is noise on a tool used many times a day, so
    /// the default marks the icon instead.
    /// </summary>
    [Fact]
    public void Decide_DefaultPreference_SuccessOnlyFlashesTheIcon()
    {
        Assert.Equal(NotificationAction.FlashOnly, SendNotifier.Decide(NotificationMode.FailuresOnly, 0));
    }

    [Fact]
    public void Decide_DefaultPreference_FailureIsShown()
    {
        Assert.Equal(NotificationAction.ShowFailure, SendNotifier.Decide(NotificationMode.FailuresOnly, 1));
    }

    [Fact]
    public void Decide_Always_ShowsSuccessToo()
    {
        Assert.Equal(NotificationAction.ShowSuccess, SendNotifier.Decide(NotificationMode.Always, 0));
        Assert.Equal(NotificationAction.ShowFailure, SendNotifier.Decide(NotificationMode.Always, 2));
    }

    [Fact]
    public void Decide_Never_StaysSilentEvenOnFailure()
    {
        Assert.Equal(NotificationAction.Nothing, SendNotifier.Decide(NotificationMode.Never, 0));
        Assert.Equal(NotificationAction.Nothing, SendNotifier.Decide(NotificationMode.Never, 3));
    }

    // --- Wording ---------------------------------------------------------------------

    [Fact]
    public void DescribeSuccess_SingleDevice_NamesIt()
    {
        Assert.Equal("Pixel 8 received it.", SendNotifier.DescribeSuccess([Ok("Pixel 8")]));
    }

    [Fact]
    public void DescribeSuccess_SeveralDevices_CountsThem()
    {
        Assert.Equal("All 3 devices received it.",
            SendNotifier.DescribeSuccess([Ok("A"), Ok("B"), Ok("C")]));
    }

    /// <summary>
    /// A rejected key and an unreachable device need different things from the user, so
    /// they must not be phrased the same way.
    /// </summary>
    [Fact]
    public void DescribeFailure_RejectedKey_SaysToPairAgain()
    {
        var text = SendNotifier.DescribeFailure([Unauthorized("Pixel 8")]);

        Assert.Contains("Pixel 8", text);
        Assert.Contains("Pair it again", text);
    }

    [Fact]
    public void DescribeFailure_Unreachable_GivesTheReason()
    {
        var text = SendNotifier.DescribeFailure([Unreachable("Pixel 8", "Connection timed out.")]);

        Assert.Contains("Pixel 8", text);
        Assert.Contains("could not be reached", text);
        Assert.Contains("Connection timed out.", text);
        Assert.DoesNotContain("Pair it again", text);
    }

    [Fact]
    public void DescribeFailure_SeveralUnreachable_NamesThemAll()
    {
        var text = SendNotifier.DescribeFailure([Unreachable("Pixel"), Unreachable("Tablet")]);

        Assert.Contains("Pixel", text);
        Assert.Contains("Tablet", text);
    }

    [Fact]
    public void DescribeFailure_AllRejectedTheKey_SaysToPairThemAgain()
    {
        var text = SendNotifier.DescribeFailure([Unauthorized("Pixel"), Unauthorized("Tablet")]);

        Assert.Contains("Pair them again", text);
    }

    [Fact]
    public void FailureTitle_EverythingFailed_SaysNothingWasSent()
    {
        Assert.Equal("Fling could not send", SendNotifier.FailureTitle([Unreachable("Pixel")], total: 1));
    }

    [Fact]
    public void FailureTitle_PartialFailure_SaysSomeWentThrough()
    {
        Assert.Equal("Fling sent to some devices",
            SendNotifier.FailureTitle([Unreachable("Tablet")], total: 3));
    }

    // --- Balloon limits --------------------------------------------------------------

    /// <summary>
    /// A balloon truncates silently past 255 characters, so many failing devices must
    /// not push the useful part off the end.
    /// </summary>
    [Fact]
    public void DescribeFailure_ManyDevices_StaysWithinBalloonLimits()
    {
        var many = Enumerable.Range(1, 20).Select(i => Unreachable($"Device number {i}")).ToList();

        var text = SendNotifier.DescribeFailure(many);

        Assert.True(text.Length <= 255, $"Failure text was {text.Length} characters.");
    }

    [Fact]
    public void DescribeFailure_LongDeviceNameAndError_StaysWithinBalloonLimits()
    {
        var text = SendNotifier.DescribeFailure(
            [Unreachable(new string('N', 80), new string('E', 200))]);

        Assert.True(text.Length <= 255, $"Failure text was {text.Length} characters.");
    }
}
