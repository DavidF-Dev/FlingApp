# Fling - Tray App

Part of [Fling](../README.md). A Windows tray application that sends clipboard content
to paired Android devices running the Fling app over the local network.

There is also a [command line tool](../cli/README.md). The two are peer front-ends over
the same shared libraries: neither requires the other, both read the same paired devices
and settings, and they release separately with their own version numbers.

[Download the latest release](https://github.com/DavidF-Dev/FlingApp/releases) — extract
`FlingTray.exe` anywhere and run it.

## Requirements

- Windows 10 or newer (64-bit). No .NET runtime needed — the published executable is
  self-contained.

## Using it

Running `FlingTray.exe` always opens the Fling window, whether that starts the app or
brings the running one forward. Whatever you last copied is staged when the window
opens, so the common case is launch, then Enter.

- **Ctrl+V** picks up something copied after the window opened.
- **Drag a file on**, or use *Choose file…*.
- **Enter** sends, **Esc** closes.
- Text and rich text can be edited before sending.

Fling sends things you can paste. Files that cannot be pasted on a phone are refused
rather than sent as a file path. Content another app marked as private — a password
manager entry, typically — is not staged automatically; Ctrl+V sends it deliberately.

The tray menu has the Fling window, a **Device manager** for pairing and live device
reachability, **Settings**, and **Quit**.

## Building

Requires .NET 8 SDK. From the `gui/` directory:

```
dotnet build Fling.Gui.slnx -c Release
dotnet test Fling.Gui.slnx               # unit tests (xUnit)
```

### Publishing

```powershell
.\scripts\publish.ps1                # build + test + package
.\scripts\publish.ps1 -SkipTests     # skip the test gate
```

Produces `dist/fling-tray-<version>-win-x64.zip` containing a self-contained
`FlingTray.exe`. Quit any running instance first — it holds a lock on the executable.

Unlike the CLI, the tray app is not trimmed: WPF is not trim-compatible. It needs no
subsystem patching either, being a GUI-subsystem binary already.

See [CHANGELOG.md](CHANGELOG.md) for what's in each release.

## Configuration

Shares `%APPDATA%\Fling\config.json` with the CLI — paired devices, maximum payload
size, compression, this PC's name, and logging.

Tray-only preferences live in `%APPDATA%\Fling\gui.json`, which the CLI never reads or
writes.

## Licence

MIT. See [LICENSE](../LICENSE).
