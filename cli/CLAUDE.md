# Fling PC Side

The .NET solution for the PC half of Fling: shared core libraries plus two front-ends — a command-line tool and a WPF tray app — that send content to paired Android devices.

The directory is named `cli/` for historical reasons; it holds the whole PC-side solution, not just the CLI.

## Projects

| Project | Target | Contents |
|---------|--------|----------|
| `Fling.Core` | `net8.0` | Config, protocol, content encoding, discovery, send orchestration. |
| `Fling.Windows` | `net8.0-windows` | Windows implementations of Core's platform interfaces: clipboard, image encoding, Explorer "Send to", startup registration. |
| `Fling` | `net8.0-windows` | CLI (`fling.exe` / `flingw.exe`). |
| `Fling.Gui` | `net8.0-windows` | WPF tray app (`FlingTray.exe`). |

`Fling.Core` targets `net8.0` deliberately — the target framework is what enforces that no Win32, COM, WinForms, or `System.Drawing` dependency reaches shared logic. Put platform code in `Fling.Windows` behind an interface instead of retargeting Core.

Front-ends are peers. Neither shells out to the other; both consume Core directly. Orchestration belongs in Core and returns structured results — front-ends decide how to present them.

## Architecture

- Both executables are published as single self-contained executables (compressed, no runtime dependency).
- Uses `HttpClient` to POST content to the Android app's HTTP server.
- Shared configuration in `%APPDATA%\Fling\config.json`. Access is guarded by a cross-process named mutex and written via temp file + atomic replace — the file holds the API keys and a resident tray app makes concurrent access with CLI invocations real.
- Tray app preferences in `%APPDATA%\Fling\gui.json`, owned exclusively by `Fling.Gui`.
- Supports multiple paired devices. The CLI requires explicit `--device` or `--all` targeting with no default; the tray app defaults to all devices, which its mandatory preview and explicit send press make safe.

## Commands

```
fling pair <ip:port>          # Pair with a new device
fling send --clipboard        # Send current clipboard contents
fling send --image <path>     # Send an image file
fling send --text "content"   # Send literal text
fling send --device <name>    # Target a specific paired device
fling send --all              # Send to all paired devices
fling status                  # Check device reachability
fling config                  # Show/edit configuration
```

## Tray App

Tray menu: Fling…, Device manager, Settings, Quit.

- **Fling window** — stages one item from the clipboard (auto-staged on open), a file picker, or drag-drop; previews it; sends to one device or all. Enter sends, Esc closes.
- **Device manager** — paired device list with live reachability, plus pairing via repeating UDP discovery. Polling runs only while the window is open.
- **Settings** — shared Fling settings and app preferences, grouped separately so it is clear which affect the CLI too.

WPF specifics that are easy to get wrong: set `ShutdownMode.OnExplicitShutdown` (the default kills the app when the last window closes), dispose the `NotifyIcon` on exit, and keep one live instance per window type.

Device names sync passively from the phone, so the tray app offers no local rename — a PC-side name is overwritten on the next send.

See `docs/gui_progress.md` for the phased plan.

## Integration

Designed to work with Greenshot's External Command Plugin:
```
fling send --image "{0}"
```

## Content Handling

- Text: sent as-is, GZip compressed at HTTP layer.
- Images: converted to PNG if needed, sent without additional compression.
- Max payload size: configurable (default 10MB).
- Binary files that are neither image nor text fall back to sending the **file path as text**. This backstops Explorer "Send to", where the alternative is nothing. It is CLI-only — the tray app rejects those types rather than delivering a useless string behind a preview.
- Clipboard content carrying the sensitive-content formats that password managers set is never staged by the tray app.

## Conventions

- Keep the build at **0 warnings**.