# Fling — CLI Tool

The Windows half of [Fling](../README.md). A command-line tool that sends clipboard
content to paired Android devices running the Fling app over the local network.

## Requirements

- Windows 10 or newer (64-bit).

## Building

Requires .NET 8 SDK. From the `cli/` directory:

```
dotnet build Fling.slnx -c Release
dotnet test Fling.slnx               # unit tests (xUnit)
```

### Publishing

```powershell
.\scripts\publish.ps1                # build + test + package
.\scripts\publish.ps1 -SkipTests     # skip the test gate
```

Produces `dist/fling-<version>-win-x64.zip` containing two self-contained executables
(no .NET runtime needed):

- **`fling.exe`** — console app for terminal use.
- **`flingw.exe`** — GUI-subsystem variant (no console window). Use this when invoking
  from a GUI caller like Greenshot or a tray app.

Both are identical except for the PE subsystem header.
See [CHANGELOG.md](CHANGELOG.md) for what's in each release.

## Usage

```
fling pair <ip[:port]>          # pair with a device
fling pair --discover           # find and pair via network discovery
fling send --clipboard --all    # send clipboard contents
fling send --image <path> --all # send an image file
fling send --text "hello" --all # send literal text
fling status                    # check device reachability
fling config show               # view configuration
fling config set --hostname "X" # set PC name sent to devices
fling --help                    # full help for any command
```

### Auto-discovery

The CLI automatically discovers paired devices on the local network via UDP broadcast.
If a phone's IP changes (e.g., switching Wi-Fi networks), `fling send` and `fling status`
will find it without re-pairing. Discovered addresses are cached for 60 seconds and
silently saved to config as a fallback for when discovery is unavailable.

### Greenshot integration

Configure Greenshot's External Command Plugin:

- **Command:** `C:\path\to\flingw.exe`
- **Arguments:** `send --image "{0}" --all`

Use `flingw.exe` (not `fling.exe`) to avoid a console window flash on each capture.

### Logging

Enable file logging to diagnose issues when invoked by a third party (e.g., Greenshot):

```
fling config set --log true
```

Logs are written to `%APPDATA%\Fling\fling.log`.

## License

See [LICENSE](../LICENSE).
