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
┌─────────────────┐         HTTP POST          ┌─────────────────────┐
│   Windows PC    │ ──────────────────────────► │    Android Phone    │
│                 │        Local Network        │                     │
│  CLI tool       │                             │  Foreground Service │
│  (fling.exe)    │                             │  (Ktor HTTP server) │
└─────────────────┘                             └─────────────────────┘
```

### PC Side: CLI Tool

- .NET 8, C#, single-file self-contained executable.
- Primary interface: command-line (`fling send --clipboard`, `fling send --image <path>`).
- Integrates with Greenshot via External Command Plugin.
- Supports multiple paired devices with a default target.
- Configuration: `%APPDATA%\Fling\config.json`.
- Future: optional tray app wrapping the same logic with clipboard watching.

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
  "status": "ok"
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

## Pairing Flow

1. User installs Fling on phone, opens app, notes the displayed IP and port.
2. On PC, user runs: `fling pair 192.168.1.50:7291`
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
  "hostName": ""
}
```

## Content Types

### Supported (MVP)

| Type | MIME | Notes |
|------|------|-------|
| Plain text | `text/plain` | GZip compressed in transit |
| Rich text | `text/html` | GZip compressed in transit |
| Image | `image/png` | Converted to PNG before sending if needed |

### Explicitly Excluded

- Files (use LocalSend for that)
- URIs / links (send as plain text if needed)
- Arbitrary binary data

## Security

- Shared API key exchanged during pairing, stored on both sides.
- All requests require the key in `X-Fling-Key` header.
- Pairing requires explicit user approval on the phone.
- Plain HTTP for MVP (acceptable on trusted local network).
- Future: HTTPS with self-signed cert exchanged during pairing.
- Future: auto-discovery (mDNS) and QR code pairing.

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
| Device names must be unique | Name is the stable identifier for future mDNS auto-discovery (Phase 10). Duplicate names would make auto-healing ambiguous. |
| Re-pair generates a new API key | Safer than reusing old keys; the phone must accept the new pairing request regardless. |
| PC name fallback: `--name` > `config.hostName` > `Environment.MachineName` | Lets users override generic hostnames (e.g., `DESKTOP-ABC123`) persistently via config or per-command via flag. |
| `fling config set` uses typed options, not flat key/value | Leverages System.CommandLine validation; avoids hand-rolling type parsing for two settings. |
| `fling send` requires explicit content source (`--clipboard`, `--image`, `--text`) | No implicit default — prevents accidentally sending stale clipboard content. Error message lists available options. |
| Greenshot uses `send --image "{0}"`, no bare positional shorthand | One-time config; adding file-path detection adds complexity for no real UX gain. |

## Future Considerations

- **Tray app**: GUI wrapper with clipboard watching (auto-sync mode), connection status, settings.
- **Two-way sync**: Android sends clipboard back to PC.
- **Auto-discovery**: mDNS/Bonjour for finding devices without manual IP entry.
- **QR code pairing**: Scan from phone to pair instantly.
- **HTTPS**: Self-signed certificate exchanged during pairing for encrypted transport.
- **Configurable file-copy exclusion on Explorer clipboard**: If user copies a file in Explorer, either skip silently or send the filename as text.
- **Default device opt-in**: `fling send --default` to send to a designated default device without specifying its name. Deferred; currently requires explicit `--device <name>` or `--all`.
