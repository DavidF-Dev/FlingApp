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

Produces `dist/fling-<version>-win-x64.zip` containing a self-contained `fling.exe`
(no .NET runtime needed). See [CHANGELOG.md](CHANGELOG.md) for what's in each release.

## Usage

```
fling pair <ip[:port]>          # pair with a device
fling send --clipboard --all    # send clipboard contents
fling send --image <path> --all # send an image file
fling send --text "hello" --all # send literal text
fling status                    # check device reachability
fling config show               # view configuration
fling --help                    # full help for any command
```

### Greenshot integration

Configure Greenshot's External Command Plugin:

- **Command:** `C:\path\to\fling.exe`
- **Arguments:** `send --image "{0}" --all`

### Logging

Enable file logging to diagnose issues when invoked by a third party (e.g., Greenshot):

```
fling config set --log true
```

Logs are written to `%APPDATA%\Fling\fling.log`.

## License

See [LICENSE](../LICENSE).
