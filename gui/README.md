# Fling Tray App

Windows tray application for sending clipboard content to paired Android devices running the Fling app.

> **In development.** The tray shell works; staging, pairing, and settings arrive in later phases. Use the [CLI](../cli/README.md) in the meantime.

## Requirements

Windows 10 or later. No .NET runtime needed — the published executable is self-contained.

## Building

```
dotnet build gui/Fling.Gui.slnx -c Release
```

The output is `FlingTray.exe`. It shares `Fling.Core` and `Fling.Windows` with the CLI, so both front-ends speak the same protocol and read the same configuration.

## Relationship to the CLI

Neither executable requires the other. The tray app is a second front-end over the same libraries, not a wrapper around `fling.exe`.

Both read `%APPDATA%\Fling\config.json` for devices and shared settings. Tray-only preferences live in `%APPDATA%\Fling\gui.json`, which the CLI never touches.

## Licence

MIT. See [LICENSE](../LICENSE).
