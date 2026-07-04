# Fling Android App

Lightweight Android app that receives clipboard content from a paired PC over the local network.

## Architecture

- Kotlin with Jetpack Compose (Material 3, Light/Dark/System theme).
- Ktor embedded HTTP server running inside a foreground service.
- Jetpack DataStore for preferences (API keys, port, settings).
- No database — recent items held in a small in-memory buffer.
- Min SDK: 26.

## Screens

1. **Main/Status** — connection status, on/off toggle, last received item preview.
2. **Settings** — port number, notification timeout, paired devices list, unpair.

## Behavior

- Foreground service with persistent notification ("Fling is listening on port 7291").
- On content received: shows a notification with preview.
- User taps notification to copy content to clipboard.
- Notification auto-expires after configurable timeout (default 5 minutes).
- Rate limiting: rejects requests exceeding N per minute.

## Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/pair` | POST | Accept or reject pairing request |
| `/clip` | POST | Receive clipboard content |
| `/ping` | GET | Health check |

## Security

- All requests (except `/pair`) require `X-Fling-Key` header with shared API key.
- Pairing requires explicit user confirmation via dialog.
