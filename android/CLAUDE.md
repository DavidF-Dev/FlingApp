# Fling Android App

Lightweight Android app that receives clipboard content from a paired PC over the local network.

## Architecture

- Kotlin with Jetpack Compose (Material 3, Light/Dark/System theme).
- Ktor embedded HTTP server running inside a foreground service.
- Jetpack DataStore for preferences (port, device name, service enabled).
- Plain JSON file for paired devices (kotlinx-serialization).
- No database — recent items held in a small in-memory buffer.
- Min SDK: 26.

## Screens

1. **Main/Status** — service toggle, device name, IP:port, Wi-Fi warning, paired devices list (tap to unpair), recent clips list (tap for Copy/Share/Clear dialog, Clear All button).
2. **Settings** — device name (free-text + regenerate), port (numeric, restart required), battery optimization status/button.

## Behavior

- Foreground service with persistent notification ("Fling is running"), hidden from lock screen (`VISIBILITY_SECRET`).
- On content received: notification with preview + Copy and Share action buttons. Tap notification body to copy and dismiss.
- Notification auto-expires after 5 minutes (`setTimeoutAfter`).
- Rate limiting: 10 requests per minute per API key (sliding window).
- Boot auto-start via `BOOT_COMPLETED` receiver if service was previously enabled.
- Wi-Fi awareness: warning in status card when not on Wi-Fi (live updates via `NetworkCallback`).
- UDP discovery listener on port 7290: responds to `FLING?` broadcasts with `FLING:<port>:<device_name>`. Gated on Wi-Fi via `ConnectivityObserver`; acquires `MulticastLock` only while listening.
- Device name sync: reads `X-Fling-Name` header from authenticated requests to update stored PC names; includes phone name in `/clip` response for CLI-side sync.
- Device name is read dynamically per request via a `suspend () -> String` provider — changes in Settings take effect without restarting the service (port still requires restart).

## Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/pair` | POST | Accept or reject pairing request |
| `/clip` | POST | Receive clipboard content |
| `/ping` | GET | Health check |

## Security

- All requests (except `/pair`) require `X-Fling-Key` header with shared API key.
- Pairing requires explicit user confirmation via notification actions.

## Build

- Release signing via git-ignored `keystore.properties` (falls back to debug signing).
- R8/ProGuard enabled for release builds with keep rules in `proguard-rules.pro`.
- APK naming: `fling-debug.apk` / `fling-release.apk` via `base.archivesName`.
- Device install script: `scripts/install-device.ps1`.
