using System.CommandLine;
using Fling.Commands;
using Fling.Config;
using Fling.Content;

var store = new ConfigStore();
var clipboardReader = new WindowsClipboardReader();

var rootCommand = new RootCommand("Fling — send clipboard content from PC to phone");
rootCommand.Subcommands.Add(ConfigCommand.Create(store));
rootCommand.Subcommands.Add(PairCommand.Create(store));
rootCommand.Subcommands.Add(SendCommand.Create(store, clipboardReader));
rootCommand.Subcommands.Add(StatusCommand.Create(store));

FlingLog? log = null;
try
{
    var config = store.Load();
    log = new FlingLog(config.Log);
}
catch
{
    // Config load failure is handled by the commands themselves.
}

// Tee stderr so error messages appear in both the console and the log.
var originalErr = Console.Error;
var errCapture = new StringWriter();
var teeWriter = new TeeTextWriter(originalErr, errCapture);
Console.SetError(teeWriter);

var exitCode = await rootCommand.Parse(args).InvokeAsync();

Console.SetError(originalErr);
var detail = errCapture.ToString().Trim();
log?.Write(args, exitCode, detail.Length > 0 ? detail : null);

return exitCode;

/// <summary>
/// Writes to two TextWriters simultaneously.
/// </summary>
file sealed class TeeTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
{
    public override System.Text.Encoding Encoding => primary.Encoding;

    public override void Write(char value)
    {
        primary.Write(value);
        secondary.Write(value);
    }

    public override void Write(string? value)
    {
        primary.Write(value);
        secondary.Write(value);
    }

    public override void WriteLine(string? value)
    {
        primary.WriteLine(value);
        secondary.WriteLine(value);
    }

    public override void Flush()
    {
        primary.Flush();
        secondary.Flush();
    }
}
