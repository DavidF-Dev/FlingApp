namespace Fling.Config;

/// <summary>
/// Append-only file log at %APPDATA%\Fling\fling.log.
/// One line per invocation. Disabled unless config.Log is true.
/// </summary>
public sealed class FlingLog
{
    private const int MaxLines = 2000;
    private const int TrimToLines = 1000;

    private readonly string _logPath;
    private readonly bool _enabled;

    public FlingLog(bool enabled)
        : this(enabled, DefaultLogPath())
    {
    }

    internal FlingLog(bool enabled, string logPath)
    {
        _enabled = enabled;
        _logPath = logPath;
    }

    private static string DefaultLogPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Fling", "fling.log");
    }

    public void Write(string[] args, int exitCode, string? detail = null)
    {
        if (!_enabled)
            return;

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var command = args.Length > 0 ? string.Join(' ', args) : "(no args)";
            var line = detail is not null
                ? $"{timestamp}  {command}  →  {detail}  exit={exitCode}"
                : $"{timestamp}  {command}  exit={exitCode}";

            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, line + Environment.NewLine);

            TrimIfNeeded();
        }
        catch
        {
            // Logging must never crash the tool.
        }
    }

    private void TrimIfNeeded()
    {
        try
        {
            var lines = File.ReadAllLines(_logPath);
            if (lines.Length <= MaxLines)
                return;

            var trimmed = lines[^TrimToLines..];
            File.WriteAllLines(_logPath, trimmed);
        }
        catch
        {
            // Best-effort.
        }
    }
}
