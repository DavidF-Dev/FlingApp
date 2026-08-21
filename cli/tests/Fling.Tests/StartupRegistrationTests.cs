using Fling.Platform;
using Microsoft.Win32;

namespace Fling.Tests;

/// <summary>
/// Exercises the real registry, but under a scratch key rather than the live Run key, so
/// a test run can never alter what actually launches at sign-in.
/// </summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _runKey;
    private readonly string _approvedKey;
    private readonly string _exePath;

    private const string ParentKey = @"Software\Fling.Tests";

    public StartupRegistrationTests()
    {
        _root = $@"{ParentKey}\{Guid.NewGuid():N}";
        _runKey = $@"{_root}\Run";
        _approvedKey = $@"{_root}\Approved";
        _exePath = Path.Combine(Path.GetTempPath(), "FlingTray.exe");
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

        // Leave nothing behind in the user's registry, not even an empty parent.
        try
        {
            using var parent = Registry.CurrentUser.OpenSubKey(ParentKey);
            if (parent?.SubKeyCount == 0)
                Registry.CurrentUser.DeleteSubKey(ParentKey, throwOnMissingSubKey: false);
        }
        catch (Exception)
        {
            // A concurrent run may have removed it first.
        }
    }

    private StartupRegistration Registration(string? exePath = null, string arguments = "--minimized") =>
        new(_runKey, _approvedKey, "Fling", exePath ?? _exePath, arguments);

    private string RegisteredValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey);
        return (string)key!.GetValue("Fling")!;
    }

    private string CreateExecutable(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fling-exe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        return path;
    }

    [Fact]
    public void IsEnabled_NothingRegistered_IsFalse()
    {
        Assert.False(Registration().IsEnabled());
    }

    [Fact]
    public void Enable_ThenIsEnabled_IsTrue()
    {
        var registration = Registration();

        registration.Enable();

        Assert.True(registration.IsEnabled());
    }

    [Fact]
    public void Enable_RecordsTheExecutablePath()
    {
        Registration().Enable();

        Assert.Contains("FlingTray.exe", RegisteredValue());
    }

    [Fact]
    public void Enable_QuotesThePathAndAppendsArguments()
    {
        var spaced = Path.Combine(Path.GetTempPath(), "Fling Tray", "FlingTray.exe");

        Registration(spaced).Enable();

        Assert.Equal($"\"{spaced}\" --minimized", RegisteredValue());
    }

    [Fact]
    public void Enable_NoArguments_RecordsOnlyTheQuotedPath()
    {
        Registration(arguments: "").Enable();

        Assert.Equal($"\"{_exePath}\"", RegisteredValue());
    }

    /// <summary>
    /// The recorded command carries arguments, so a naive read of the whole value as a
    /// path would never match.
    /// </summary>
    [Fact]
    public void IsEnabled_CommandWithArguments_StillMatches()
    {
        var registration = Registration();
        registration.Enable();

        Assert.Contains("--minimized", RegisteredValue());
        Assert.True(registration.IsEnabled());
    }

    [Theory]
    [InlineData("\"C:\\Apps\\FlingTray.exe\" --minimized", "C:\\Apps\\FlingTray.exe")]
    [InlineData("\"C:\\Program Files\\Fling\\FlingTray.exe\"", "C:\\Program Files\\Fling\\FlingTray.exe")]
    [InlineData("C:\\Apps\\FlingTray.exe --minimized", "C:\\Apps\\FlingTray.exe")]
    [InlineData("C:\\Apps\\FlingTray.exe", "C:\\Apps\\FlingTray.exe")]
    [InlineData("  \"C:\\Apps\\FlingTray.exe\"  ", "C:\\Apps\\FlingTray.exe")]
    public void ParseExecutablePath_ReadsTheExecutable(string command, string expected)
    {
        Assert.Equal(expected, StartupRegistration.ParseExecutablePath(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"")]
    [InlineData("\"\"")]
    public void ParseExecutablePath_Unreadable_ReturnsNull(string command)
    {
        Assert.Null(StartupRegistration.ParseExecutablePath(command));
    }

    [Fact]
    public void IsEnabled_MalformedCommand_IsFalseRatherThanThrowing()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_runKey))
            key.SetValue("Fling", "\"", RegistryValueKind.String);

        Assert.False(Registration().IsEnabled());
    }

    [Fact]
    public void Disable_RemovesTheEntry()
    {
        var registration = Registration();
        registration.Enable();

        registration.Disable();

        Assert.False(registration.IsEnabled());
        Assert.Null(registration.RegisteredCommand());
    }

    [Fact]
    public void Disable_WhenNotRegistered_DoesNotThrow()
    {
        Registration().Disable();
    }

    [Fact]
    public void Enable_Twice_IsIdempotent()
    {
        var registration = Registration();

        registration.Enable();
        registration.Enable();

        Assert.True(registration.IsEnabled());
    }

    /// <summary>
    /// Moving or reinstalling the app leaves a Run entry pointing at a path that no
    /// longer starts anything, which must not be reported as enabled.
    /// </summary>
    [Fact]
    public void IsEnabled_EntryPointsElsewhere_IsFalse()
    {
        Registration(Path.Combine(Path.GetTempPath(), "old", "FlingTray.exe")).Enable();

        Assert.False(Registration().IsEnabled());
    }

    [Fact]
    public void IsEnabled_DisabledInTaskManager_IsFalse()
    {
        var registration = Registration();
        registration.Enable();

        WriteApprovalState(disabled: true);

        Assert.False(registration.IsEnabled());
        Assert.NotNull(registration.RegisteredCommand());
    }

    [Fact]
    public void IsEnabled_ApprovedInTaskManager_IsTrue()
    {
        var registration = Registration();
        registration.Enable();

        WriteApprovalState(disabled: false);

        Assert.True(registration.IsEnabled());
    }

    /// <summary>
    /// Re-enabling from the app has to clear a refusal recorded elsewhere, or the
    /// checkbox would turn itself back off.
    /// </summary>
    [Fact]
    public void Enable_AfterTaskManagerDisabledIt_TakesEffect()
    {
        var registration = Registration();
        registration.Enable();
        WriteApprovalState(disabled: true);
        Assert.False(registration.IsEnabled());

        registration.Enable();

        Assert.True(registration.IsEnabled());
    }

    // --- Repairing a stale entry -----------------------------------------------------

    /// <summary>
    /// An entry written before the app needed a switch launches without it, which is how
    /// a sign-in launch ended up opening a window instead of going to the tray. The path
    /// is still valid, so nothing about it looks wrong.
    /// </summary>
    [Fact]
    public void RepairIfStale_ArgumentsChangedSinceTheEntryWasWritten_RewritesIt()
    {
        Registration(arguments: "").Enable();
        Assert.DoesNotContain("--minimized", RegisteredValue());

        var current = Registration(arguments: "--minimized");

        Assert.True(current.RepairIfStale());
        Assert.Equal($"\"{_exePath}\" --minimized", RegisteredValue());
        Assert.True(current.IsEnabled());
    }

    [Fact]
    public void RepairIfStale_ArgumentsRemovedInThisBuild_RewritesIt()
    {
        Registration(arguments: "--minimized").Enable();

        Assert.True(Registration(arguments: "").RepairIfStale());
        Assert.Equal($"\"{_exePath}\"", RegisteredValue());
    }

    /// <summary>
    /// Repairing arguments must not resurrect an entry the user switched off elsewhere.
    /// </summary>
    [Fact]
    public void RepairIfStale_StaleArgumentsButDisabledInTaskManager_StaysDisabled()
    {
        Registration(arguments: "").Enable();
        WriteApprovalState(disabled: true);

        var current = Registration(arguments: "--minimized");

        Assert.True(current.RepairIfStale());
        Assert.Contains("--minimized", RegisteredValue());
        Assert.False(current.IsEnabled());
    }

    // --- Repairing a moved executable ------------------------------------------------

    [Fact]
    public void RepairIfStale_NoEntry_DoesNothing()
    {
        var registration = Registration();

        Assert.False(registration.RepairIfStale());
        Assert.Null(registration.RegisteredCommand());
        Assert.False(registration.IsEnabled());
    }

    [Fact]
    public void RepairIfStale_EntryAlreadyCorrect_DoesNothing()
    {
        var registration = Registration();
        registration.Enable();
        var before = RegisteredValue();

        Assert.False(registration.RepairIfStale());
        Assert.Equal(before, RegisteredValue());
    }

    /// <summary>
    /// The app was moved or reinstalled elsewhere, so the entry names a file that is no
    /// longer there and would launch nothing at sign-in.
    /// </summary>
    [Fact]
    public void RepairIfStale_OldPathGone_RepointsAtTheRunningCopy()
    {
        var oldPath = Path.Combine(Path.GetTempPath(), $"fling-gone-{Guid.NewGuid():N}", "FlingTray.exe");
        Registration(oldPath).Enable();

        var current = Registration();

        Assert.True(current.RepairIfStale());
        Assert.True(current.IsEnabled());
        Assert.Contains(_exePath, RegisteredValue());
    }

    [Fact]
    public void RepairIfStale_PreservesArguments()
    {
        var oldPath = Path.Combine(Path.GetTempPath(), $"fling-gone-{Guid.NewGuid():N}", "FlingTray.exe");
        Registration(oldPath).Enable();

        Registration().RepairIfStale();

        Assert.Contains("--minimized", RegisteredValue());
    }

    /// <summary>
    /// Two copies on disk: the registered one still works, so this one must not take the
    /// entry from it.
    /// </summary>
    [Fact]
    public void RepairIfStale_OtherCopyStillExists_LeavesTheEntryAlone()
    {
        var otherCopy = CreateExecutable("FlingTray.exe");
        try
        {
            Registration(otherCopy).Enable();
            var before = RegisteredValue();

            var current = Registration();

            Assert.False(current.RepairIfStale());
            Assert.Equal(before, RegisteredValue());
            Assert.False(current.IsEnabled());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(otherCopy)!, recursive: true);
        }
    }

    /// <summary>
    /// Correcting a path is not consent to start again — a user who turned the entry off
    /// in Task Manager should find it still off.
    /// </summary>
    [Fact]
    public void RepairIfStale_DoesNotOverrideATaskManagerRefusal()
    {
        var oldPath = Path.Combine(Path.GetTempPath(), $"fling-gone-{Guid.NewGuid():N}", "FlingTray.exe");
        Registration(oldPath).Enable();
        WriteApprovalState(disabled: true);

        var current = Registration();

        Assert.True(current.RepairIfStale());
        Assert.False(current.IsEnabled());
    }

    [Fact]
    public void RepairIfStale_NeverOptsTheUserIn()
    {
        var registration = Registration();

        registration.RepairIfStale();
        registration.RepairIfStale();

        Assert.Null(registration.RegisteredCommand());
    }

    private void WriteApprovalState(bool disabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_approvedKey);
        var state = new byte[12];
        state[0] = disabled ? (byte)0x03 : (byte)0x02;
        key.SetValue("Fling", state, RegistryValueKind.Binary);
    }
}
