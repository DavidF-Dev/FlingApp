using Microsoft.Win32;

namespace Fling.Platform;

/// <summary>
/// Whether an application launches when the user signs in.
/// </summary>
public interface IStartupRegistration
{
    bool IsEnabled();
    void Enable();
    void Disable();
    bool HealIfMoved();
}

/// <summary>
/// Registers the application in the per-user Run key.
/// </summary>
/// <remarks>
/// Uses the registry rather than a Startup-folder shortcut: it needs no elevation, no
/// COM, and it is what Task Manager's Startup tab lists and controls. That tab does not
/// delete the Run value when a user disables an entry — it records the refusal
/// separately — so the reported state has to account for both places.
/// </remarks>
public sealed class StartupRegistration : IStartupRegistration
{
    private const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string DefaultValueName = "Fling";

    private readonly string _runKey;
    private readonly string _approvedKey;
    private readonly string _valueName;
    private readonly string _executablePath;
    private readonly string _arguments;

    public StartupRegistration(string arguments = "")
        : this(DefaultRunKey, DefaultApprovedKey, DefaultValueName, Environment.ProcessPath!, arguments)
    {
    }

    public StartupRegistration(
        string runKey,
        string approvedKey,
        string valueName,
        string executablePath,
        string arguments = "")
    {
        _runKey = runKey;
        _approvedKey = approvedKey;
        _valueName = valueName;
        _executablePath = executablePath;
        _arguments = arguments;
    }

    /// <summary>
    /// True only when the entry exists, points at this executable, and has not been
    /// switched off elsewhere.
    /// </summary>
    public bool IsEnabled()
    {
        var command = RegisteredCommand();
        if (command is null)
            return false;

        if (!PointsAtThisExecutable(command))
            return false;

        return !IsDisabledByTaskManager();
    }

    /// <summary>
    /// The command currently registered, or null when there is no entry.
    /// </summary>
    public string? RegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey);
        return key?.GetValue(_valueName) as string;
    }

    public void Enable()
    {
        WriteCommand();

        // A stale refusal recorded by Task Manager would otherwise keep the entry off.
        ClearTaskManagerRefusal();
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Repoints an existing entry at the running executable when the one it names has
    /// gone, and reports whether it did.
    /// </summary>
    /// <remarks>
    /// Moving or reinstalling the app leaves an entry that launches nothing, and the
    /// user's intent — start at sign-in — was never about a particular file. Healing is
    /// deliberately narrow: an entry is never created here, so the on/off choice stays
    /// the user's, and an entry naming a copy that still exists is left alone rather
    /// than stolen from it. A refusal recorded by Task Manager is preserved, since
    /// fixing a path is not consent to start again.
    /// </remarks>
    public bool HealIfMoved()
    {
        var command = RegisteredCommand();
        if (command is null)
            return false;

        if (PointsAtThisExecutable(command))
            return false;

        var registered = ParseExecutablePath(command);
        if (registered is not null && File.Exists(registered))
            return false;

        WriteCommand();
        return true;
    }

    /// <summary>
    /// Extracts the executable from a Run command line, which may be quoted and may
    /// carry arguments. Returns null when it cannot be read.
    /// </summary>
    internal static string? ParseExecutablePath(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
            return null;

        if (trimmed[0] == '"')
        {
            var close = trimmed.IndexOf('"', 1);
            return close > 1 ? trimmed[1..close] : null;
        }

        // An unquoted path cannot contain spaces, so the first one ends it.
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    private void WriteCommand()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_runKey);
        key.SetValue(_valueName, BuildCommand(), RegistryValueKind.String);
    }

    private string BuildCommand()
    {
        var quoted = $"\"{_executablePath}\"";
        return _arguments.Length == 0 ? quoted : $"{quoted} {_arguments}";
    }

    private bool PointsAtThisExecutable(string command)
    {
        var registered = ParseExecutablePath(command);
        if (registered is null)
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(registered),
                Path.GetFullPath(_executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // A malformed path in the registry is simply not a match.
            return false;
        }
    }

    /// <summary>
    /// Task Manager records a disabled entry as a binary blob whose first byte carries
    /// the state; odd values mean the user switched it off.
    /// </summary>
    private bool IsDisabledByTaskManager()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_approvedKey);
        return key?.GetValue(_valueName) is byte[] { Length: > 0 } state && (state[0] & 1) != 0;
    }

    private void ClearTaskManagerRefusal()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_approvedKey, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }
}
