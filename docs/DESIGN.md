# Fling - Paste on Phone: Design Document

## Overview

Fling sends clipboard content from a Windows PC to an Android phone over the local network. The phone receives the content and makes it available for pasting into any app via a notification tap.

This is strictly a clipboard tool — not a file-sharing app. Content is transient, local-network-only, and notification-based.

## Objectives

1. Take a screenshot on PC (via Greenshot or otherwise) and have it arrive on the phone ready to paste into a messaging app.
2. Send any clipboard content (text, rich text, images) from PC to phone.
3. Keep received content temporary — auto-expiring notifications, no permanent storage.
4. Minimal setup: pair once, then it just works.

## Architecture

```
┌──────────────────────────────┐      HTTP POST       ┌─────────────────────┐
│         Windows PC           │ ───────────────────► │    Android Phone    │
│                              │    Local Network     │                     │
│  ┌────────────┐ ┌─────────┐  │                      │  Foreground Service │
│  │ CLI        │ │ Tray app│  │                      │  (Ktor HTTP server) │
│  │ (fling.exe)│ │(FlingTray)│                       │                     │
│  └─────┬──────┘ └────┬────┘  │                      │                     │
│        └──────┬──────┘       │                      │                     │
│           Fling.Core         │                      │                     │
└──────────────────────────────┘                      └─────────────────────┘
```

### PC Side: Shared Core

- `Fling.Core` (`net8.0`) holds configuration, protocol, content encoding, discovery, and send orchestration. No UI and no platform APIs — the target framework enforces this.
- `Fling.Windows` (`net8.0-windows`) holds Windows-specific implementations behind Core's interfaces: clipboard reading, image encoding, Explorer "Send to" integration, startup registration.
- The CLI and the tray app are peer front-ends over these libraries. Neither wraps nor requires the other at runtime.

### PC Side: CLI Tool

- .NET 8, C#, single-file self-contained executable.
- Interface: command-line (`fling send --clipboard`, `fling send --image <path>`).
- Integrates with Greenshot via External Command Plugin.
- Supports multiple paired devices (explicit `--device` or `--all` targeting, no default).
- Configuration: `%APPDATA%\Fling\config.json`.

### PC Side: Tray App

- .NET 8, WPF, single-file self-contained executable (`FlingTray.exe`).
- Tray icon with four menu items: Fling…, Device manager, Settings, Quit.
- Targets interactive use: staging and previewing content before sending, GUI pairing, live device reachability.
- Configuration: shares `config.json` with the CLI; GUI-only preferences live in `%APPDATA%\Fling\gui.json`.
- See `gui_progress.md` for the phased implementation plan.

### Android Side: App

- Kotlin, Jetpack Compose, Material 3 (Light/Dark/System theme).
- Ktor embedded HTTP server in a foreground service.
- Jetpack DataStore for preferences.
- No database — recent items in memory buffer only.
- Min SDK: 26.

## Protocol

### Transport

- HTTP over local network (plain HTTP for MVP).
- Default port: **7291**.
- Authentication: shared API key in `X-Fling-Key` header.
- Text payloads: GZip compressed at the application level (see Compression below).
- Image payloads: PNG, no additional compression.
- Max payload size: configurable (default 10MB).

### Endpoints (Android serves these)

#### `POST /pair`

Initial pairing request from CLI to phone.

Request:
```json
{
  "name": "My PC",
  "key": "<generated-api-key>"
}
```

Response (user accepted):
```json
{
  "status": "accepted",
  "name": "Pixel 8"
}
```

Response (user rejected):
```json
{
  "status": "rejected"
}
```

Pairing requires explicit user confirmation on the phone. For MVP, this is a notification with Accept/Reject action buttons. May be upgraded to a full-screen dialog later.

#### `POST /clip`

Send clipboard content. Requires `X-Fling-Key` header.

Request:
```json
{
  "type": "text/plain | text/html | image/png",
  "data": "<base64-encoded content, see Compression>",
  "timestamp": 1720100000
}
```

Response:
```json
{
  "status": "ok",
  "name": "Pixel 8"
}
```

Error responses:
- `401 Unauthorized` — missing or invalid API key.
- `413 Payload Too Large` — exceeds max size.
- `429 Too Many Requests` — rate limited.

#### `GET /ping`

Health check. Requires `X-Fling-Key` header.

Response:
```json
{
  "status": "ok",
  "name": "Pixel 8",
  "version": "1.0.0"
}
```

### Compression

Compression is applied at the **application level**, not the HTTP transport level. The `Content-Encoding` HTTP header is **not** used.

For text types (`text/plain`, `text/html`):
1. CLI takes the raw text bytes (UTF-8).
2. GZip-compresses them.
3. Base64-encodes the compressed bytes into the `data` field.
4. Sets `"compressed": true` in the JSON body.

For `image/png`:
1. CLI takes the raw PNG bytes.
2. Base64-encodes them directly (no compression — PNG is already compressed).
3. Sets `"compressed": false` (or omits the field).

The Android side reverses the process: base64-decode, then gunzip if `compressed` is `true`.

### Rate Limiting

Enforced on the Android side. Default: 10 requests per minute. Configurable. Returns `429` when exceeded.

### Discovery (UDP Broadcast)

Auto-discovery runs over UDP, separate from the HTTP protocol.

- **Discovery port:** 7290 (one below the HTTP port).
- **Request:** CLI broadcasts `FLING?` (UTF-8) to `255.255.255.255:7290`.
- **Response:** Phone responds via unicast with `FLING:<port>:<device_name>` (UTF-8).
  - `<port>` is the HTTP server port (e.g., `7291`).
  - `<device_name>` is the phone's configured name. May contain colons — the CLI parses greedily (splits on the first two colons only).
- **Timeout:** CLI waits 1.5 seconds, collecting all responses. Falls back to the stored IP if no matching device responds.
- **Caching:** Discovered addresses are cached in memory with a 60-second TTL. Subsequent commands within the TTL skip the broadcast.
- **Config update:** When a discovered IP differs from the stored IP, the CLI silently updates `config.json`. The stored IP serves as a "last known good" fallback.

## Pairing Flow

1. User installs Fling on phone, opens app, notes the displayed IP and port (or uses `fling pair --discover`).
2. On PC, user runs: `fling pair 192.168.1.50:7291` (or `fling pair --discover`)
3. CLI generates a random API key and sends `POST /pair`.
4. Phone shows a notification: "PC 'My PC' wants to connect" with Accept/Reject actions.
5. User taps Accept.
6. CLI stores device entry in config. Phone stores the key in DataStore.
7. Connection is established. Subsequent `fling send` commands use the stored key.

## Android Behavior

1. Foreground service starts with persistent notification: "Fling is listening on port 7291".
2. Content received → notification appears with preview (truncated text, or image thumbnail).
3. User taps notification → content is written to clipboard, toast confirms.
4. Notification auto-expires after configurable timeout (default: 5 minutes).
5. Recent items (last 5-10) viewable in-app if user missed a notification.
6. Buffer is in-memory only — lost on app restart (intentionally transient).

## CLI Commands

```
fling pair <ip:port>                # Pair with a new device
fling pair <ip:port> --name "PC"    # Pair with a custom PC name
fling pair <ip:port> --force        # Re-pair (new key) even if device exists
fling send --clipboard --device <n>  # Send current clipboard to a device
fling send --clipboard --all        # Send current clipboard to all devices
fling send --image <path> --all     # Send an image file
fling send --text "content" --all   # Send literal text
fling send --dry-run --clipboard    # Preview without sending
fling status                        # Check reachability of paired devices
fling config show                   # Show current configuration
fling config set --max-size 25      # Update max payload size
fling config set --compress false   # Toggle compression
fling config set --hostname "PC"    # Set PC name sent to devices
fling config remove <name>          # Remove a paired device
```

## CLI Configuration

Stored at `%APPDATA%\Fling\config.json`:

```json
{
  "devices": [
    {
      "name": "Pixel 8",
      "host": "192.168.1.50",
      "port": 7291,
      "apiKey": "a1b2c3d4..."
    }
  ],
  "maxSizeMb": 10,
  "compress": true,
  "hostName": "",
  "log": false
}
```

Written by both front-ends. Access is guarded by a cross-process named mutex and written via temp file + atomic replace — this file holds the API keys, and a long-running tray app makes concurrent access with CLI invocations real.

## GUI Application

### Tray Menu

| Item | Opens |
|------|-------|
| Fling… | Content staging window. Also opened by double-clicking the tray icon. |
| Device manager | Paired device list, live reachability, and pairing. |
| Settings | Shared Fling settings and app preferences. |
| Quit | Exits the tray app. The CLI is unaffected. |

### Fling Window

Stages exactly one item from one of four sources, previews it, and sends it.

- **Sources:** clipboard (auto-staged when the window opens), Paste button / Ctrl+V, file picker, drag-drop. A new source replaces the staged item rather than adding to it.
- **Preview:** always shown. Thumbnail with dimensions for images; editable text with character count for text. Resolved content type and payload size for both.
- **Rejected content:** file types that `FileContentResolver` classifies as `FilePath` are refused with an explanation. The path-as-text fallback exists for Explorer "Send to", where the alternative is nothing; in a window with a preview it reads as file transfer and delivers a useless string.
- **Sensitive clipboard content** — anything carrying the `ExcludeClipboardContentFromMonitorProcessing` or `CanIncludeInClipboardHistory` clipboard formats — is not staged.
- **Targeting:** All by default when more than one device is paired; the single device when only one is. Nothing is sent without an explicit Fling press (or Enter).
- **Results:** per-device. A partial failure names which device failed and distinguishes auth failure from unreachable.

### Device Manager

- Both discovery and reachability require the Fling **service** to be running on the phone — it owns the UDP discovery listener and the HTTP server. `serviceEnabled` defaults to off and is turned on from the app, the quick settings tile, or automatically at boot once enabled. Its persistent "Fling is running" notification is the signal a user can check, and the window says so wherever a device fails to appear or answer.
- Paired devices with live reachability, polled only while the window is open.
- Discovery re-broadcasts on an interval while the window is open, so phones that were asleep appear without reopening.
- Pairing is asynchronous — `POST /pair` blocks on the user tapping Accept. States: discovering → pairing (cancellable) → accepted / rejected / timed out.
- Manual `ip:port` entry as a fallback where UDP broadcast is blocked.
- No local rename: names sync passively from the phone, so a PC-side rename is overwritten on the next send.
- Removal is confirmed, and stated as one-sided — the phone keeps its entry until cleared there.

### GUI Configuration

Stored at `%APPDATA%\Fling\gui.json`, owned exclusively by the tray app. Never read or written by `Fling.Core` or the CLI:

```json
{
  "notifications": "failuresOnly",
  "rememberLastDevice": true,
  "lastDevice": "",
  "sendHtmlAsPlainText": false,
  "firstRunComplete": false
}
```

Absent by design: `runAtStartup`. `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` is the source of truth and is read live — a cached copy goes stale the moment the entry is disabled from Task Manager's Startup tab.

## Content Types

### Supported (MVP)

| Type | MIME | Notes |
|------|------|-------|
| Plain text | `text/plain` | GZip compressed in transit. Preferred whenever the clipboard offers it. |
| Rich text | `text/html` | GZip compressed in transit. Only sent when the source offers no plain-text alternative — the phone pastes it as literal markup. |
| Image | `image/png` | Converted to PNG before sending if needed |

### Explicitly Excluded

- Files (use LocalSend for that)
- URIs / links (send as plain text if needed)
- Arbitrary binary data

## Decisions Log

| Decision | Rationale |
|----------|-----------|
| CLI-first, no tray app for MVP | Simpler; Greenshot only needs a CLI tool. Tray app deferred until clipboard watching is needed. |
| Tap-to-copy (not auto-copy) | Prevents silently overwriting phone clipboard; gives user control. |
| No database on Android | Content is intentionally transient; in-memory buffer is sufficient. |
| Mono-repo | Protocol changes should be atomic across both sides; single developer. |
| Port 7291 | High port, avoids collisions with common dev servers in 8xxx range. |
| Explicit-send only (no auto clipboard sync for MVP) | Avoids accidentally sending passwords/sensitive content. Auto-sync is opt-in later via tray app. |
| Notification with auto-expiry | Content is transient; unclaimed notifications expire after 5 min. |
| Rate limiting on Android side | Phone is the resource being protected. |
| GZip for text only | Images (PNG) are already compressed; GZip adds no value there. |
| Application-level compression, not HTTP Content-Encoding | GZip is applied to raw bytes before base64 encoding. Avoids Ktor content-encoding negotiation; keeps the JSON envelope uniform. A `compressed` field in the body signals whether to gunzip. |
| Pairing via notification actions (MVP) | Simpler than launching a dialog Activity. Approval logic is behind an abstraction so the UX can be upgraded to a dialog later without changing route handlers or storage. |
| Multiple devices in config from the start | Avoids painful config migration later; minimal extra implementation effort. |
| No default device; require `--device` or `--all` | Prevents accidental broadcast of sensitive clipboard content to unintended devices. A `--default` opt-in flag may be added later. |
| Device names must be unique | Name is the stable identifier for UDP broadcast auto-discovery (Phase 11). Duplicate names would make auto-healing ambiguous. |
| Re-pair generates a new API key | Safer than reusing old keys; the phone must accept the new pairing request regardless. |
| PC name fallback: `--name` > `config.hostName` > `Environment.MachineName` | Lets users override generic hostnames (e.g., `DESKTOP-ABC123`) persistently via config or per-command via flag. |
| `fling config set` uses typed options, not flat key/value | Leverages System.CommandLine validation; avoids hand-rolling type parsing for two settings. |
| `fling send` requires explicit content source (`--clipboard`, `--image`, `--text`) | No implicit default — prevents accidentally sending stale clipboard content. Error message lists available options. |
| Greenshot uses `send --image "{0}"`, no bare positional shorthand | One-time config; adding file-path detection adds complexity for no real UX gain. |
| Opt-in file logging via `config.log` | Logs each invocation (args, exit code, error message) to `%APPDATA%\Fling\fling.log`. Off by default. Essential for debugging third-party callers (e.g., Greenshot) where stderr is not visible. Auto-trims at 2000 lines. |
| Two-exe publish: `fling.exe` + `flingw.exe` | A Windows PE exe has exactly one subsystem flag — console or GUI. Console apps flash a window when launched from a GUI caller; GUI apps lose stdout in cmd.exe. The publish script builds once (console), copies, and patches one PE byte to produce the GUI variant. Same pattern as `python.exe` / `pythonw.exe`. |
| Plain text is preferred over CF_HTML when the clipboard offers both | An application offering both is describing the same content twice, and its CF_HTML carries the styling of whatever view it was copied from — one word out of a syntax-highlighted diff arrives as hundreds of characters of span markup. The phone writes what it receives to the clipboard with `ClipData.newPlainText`, so markup is pasted verbatim, tags and all. Preferring CF_UNICODETEXT produces what every other application shows. CF_HTML remains a fallback for the rare source that offers no plain-text alternative. Rich text would only become worth sending if the phone used `ClipData.newHtmlText`. |
| Only the tray app may reference WinForms or WPF | `UseWindowsForms`/`UseWPF` add a framework reference to `Microsoft.WindowsDesktop.App`, which a self-contained publish ships whole — including all of WPF — regardless of what is called, and which makes the project untrimmable. The CLI carried both flags for two APIs (`Clipboard` and `Image`) at a measured cost of 58 MB and its ability to trim: 69 MB → 10.5 MB once removed. WinForms now exists solely for `NotifyIcon` in `Fling.Gui`. |
| PNG input is sent verbatim rather than re-encoded | Decoding and re-encoding a PNG is wasted work on the dominant path (Greenshot produces PNG), and GDI+ does not preserve the source's compression settings, so the round trip often produces a larger payload. Validation is by content — signature plus a well-formed trailing `IEND` chunk — not by file extension, so a mislabelled or truncated file still fails on the PC instead of arriving broken on the phone. |
| `System.Drawing.Common` kept as a package reference rather than replaced | It was never the cause of the size problem — the standalone GDI+ package does not pull the desktop runtime pack, and measured 10.2 MB trimmed with no trim warnings. Keeping it means image handling is unchanged and format support stays bit-identical. ImageSharp and WIC were rejected: neither preserves current behaviour, WIC's only managed wrapper lives in `PresentationCore`, and GDI+ receives image-decoder security fixes through Windows Update instead of requiring a Fling release. |
| CLI clipboard access via Win32 P/Invoke, not `System.Windows.Forms.Clipboard` | The managed wrapper is convenient but drags in the entire desktop runtime pack. Roughly 200 lines of interop against `OpenClipboard`/`GetClipboardData` removes that dependency. Format precedence (image, then HTML, then plain text) and CF_HTML fragment parsing are unchanged. |
| CLI is trimmed and ReadyToRun-compiled; tray app is neither | Measured 69.0 MB → 15.9 MB, cold start 1445 ms → 618 ms, warm start 204 ms → 151 ms. ReadyToRun is not optional alongside trimming: the trimmer rewrites framework assemblies and discards the precompiled native code they ship with, so trimming alone regressed warm start to 284 ms — worse than the untrimmed original. It costs 4.3 MB and returns 133 ms per invocation, which matters because Greenshot and Explorer "Send to" invoke the CLI repeatedly. WPF is not trim-compatible, so the tray app accepts a larger binary; the two front-ends do not share publish settings. |
| Explorer shortcut written via `IShellLink` with `[GeneratedComInterface]` | The `WScript.Shell` scripting object is reached by ProgID and called through IDispatch. The trim analyzer rejected both (IL2072, IL2050) and was right to: trimming can discard the metadata late binding and COM vtable dispatch depend on, which would fail at runtime rather than at build time. Source-generated COM marshalling removes the warning instead of suppressing it. |
| Per-operation HTTP timeouts via linked `CancellationTokenSource` | `HttpClient.Timeout` can only be assigned before the first request. Assigning it per call broke `send --all` for two or more devices, which share one client across parallel sends. |
| Source-generated `JsonSerializerContext` rather than reflection-based serialization | Trimming raises IL2026 on `JsonSerializer.Serialize<T>`, and `TreatWarningsAsErrors` makes that a build failure. Also removes the main obstacle should NativeAOT be pursued later. |
| Each component releases independently under a scoped tag (`cli/v*`, `android/v*`, `gui/v*`) | The release axis is the shipped artifact, not the source directory — the tray app shares a solution with the CLI but has its own audience, cadence, changelog, and version. Compatibility is expressed by cross-referencing the other components' latest tags in the release notes. |
| Config compatibility across front-end version skew handled by `[JsonExtensionData]` | Independent releases mean users will run an older CLI beside a newer tray app, each bundling its own copy of Core. `ConfigStore` deserializes and reserializes, so without extension data an older build silently drops fields a newer build added. Solving this in code beats relying on users to keep versions aligned. |
| Tray app is a peer front-end, not a CLI wrapper | Shelling out to `fling.exe` would mean parsing stdout for status, no progress or cancellation, and re-solving exe-path resolution. Both front-ends consume `Fling.Core` directly. Neither requires the other at runtime. |
| `Fling.Core` targets `net8.0`, not `net8.0-windows` | The target framework is what actually enforces that no Win32, COM, WinForms, or `System.Drawing` dependency leaks into shared logic. Platform code lives in `Fling.Windows` behind interfaces. Costs nothing now; means a future non-Windows port is not a rewrite. |
| WPF for the GUI | Data binding suits the device list and staging preview; single-file self-contained publish already works; tray via the WinForms `NotifyIcon` needs no third-party package. WinUI 3 was rejected for Windows App SDK deployment friction; Avalonia is only worth its dependency if cross-platform is committed to. |
| GUI preferences in a separate `gui.json`, not nested in `config.json` | Three reasons. Write frequency: GUI preferences change on nearly every interaction while `config.json` changes rarely, so merging them means constantly rewriting the file holding the API keys and contending with CLI invocations. Blast radius: a corrupt `gui.json` falls back to defaults, a corrupt `config.json` costs every pairing. Layering: `Fling.Core` is UI-agnostic by design and should not carry front-end state. |
| GUI defaults to All devices; CLI still requires explicit targeting | The CLI's no-default rule guards against silently broadcasting sensitive clipboard content. The GUI's mandatory preview and explicit send press remove that failure mode, so the safer-by-default behaviour there is the convenient one. With one paired device the selector shows that device, not "All". |
| Clipboard auto-staged when the Fling window opens | "I copied something, now send it" is the dominant case; a mandatory paste click exists only because other sources do. Nothing is transmitted without an explicit send. Content carrying the sensitive-clipboard formats is not staged at all. |
| GUI rejects file types the CLI sends as a path | `FileContentResolver` falls back to sending the file path as text for binary files, which backstops Explorer "Send to" where the alternative is nothing. In a window with a preview and a Fling button it reads as file transfer and delivers a useless string. The fallback stays CLI-only. |
| Balloon tips rather than Windows toast APIs | Toasts from an unpackaged .NET app require an AppUserModelID and a Start Menu shortcut before rendering. `NotifyIcon.ShowBalloonTip` is surfaced as a real toast on Windows 10/11 with no dependencies. Revisit if notification action buttons are needed. |
| Send notifications default to failures-only | A success toast per send is noise on a tool used many times a day. Success is signalled by a brief tray icon change; the mode is configurable (Always / Failures only / Never). |
| Run-at-startup via `HKCU\...\Run`, state read live | User-scope, no admin, no COM shortcut. It is also what Task Manager's Startup tab controls, so caching the value would produce a checkbox that lies after the user disables it there. |
| No local device rename in the GUI | Passive name sync overwrites the stored name from `/clip` and `/ping` responses, so a PC-side rename is clobbered on the next send. Renaming belongs on the phone. |
| Reachability and discovery poll only while the Device manager is open | A tray app pinging a phone all day is a battery cost with no user-visible benefit outside that window. |
| Passive device name sync | Names exchanged at pair time go stale if either side renames. PC → Phone: CLI sends `X-Fling-Name` header on `/clip` and `/ping` requests; phone updates stored PC name if it differs. Phone → PC: `/clip` and `/ping` responses include `"name"` field; CLI updates stored phone name if it differs. Discovery only handles case corrections (a full rename means the old name won't match the discovery response). Propagates on next natural interaction — no dedicated sync call. |

## Future Considerations

Deferred from the tray app's v1 (see `gui_progress.md` for what v1 must avoid foreclosing):

- **Global hotkey**: Send the clipboard without opening a window. The auto-staged Fling window already reduces the common path to open-and-Enter, so this is a marginal gain over meaningful hotkey-registration and conflict-handling work.
- **Clipboard watching (auto-sync)**: The largest safety surface in the design. Requires an explicitly armed mode, unmistakable state indication, sensitive-clipboard-format handling, and self-send suppression.
- **Send history with re-send**: The phone side is deliberately transient; a PC-side history introduces its own retention and privacy questions.

Longer-term, front-end independent:

- **Two-way sync**: Android sends clipboard back to PC. Needs a listener on the PC, a new protocol direction, and a firewall prompt.
- **HTTPS**: Self-signed certificate exchanged during pairing for encrypted transport.
- **Cross-platform**: `Fling.Core` is platform-agnostic, but a port needs per-OS clipboard access (genuinely difficult on Linux across X11 and Wayland) and a `System.Drawing` replacement. Avalonia would be the GUI answer. No demand established.
- **Additional image formats**: `.webp` is absent from the recognised image extensions and now common from browsers and screenshot tools; `.svg` currently passes the text check and would be sent as raw XML.
- **Configurable file-copy exclusion on Explorer clipboard**: If user copies a file in Explorer, either skip silently or send the filename as text.
- **Default device opt-in**: `fling send --default` to send to a designated default device without specifying its name. Deferred; currently requires explicit `--device <name>` or `--all`.
