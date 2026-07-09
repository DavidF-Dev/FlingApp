# Fling — Paste on Phone

[![Release](https://img.shields.io/github/v/release/DavidF-Dev/FlingApp?style=flat-square)](https://github.com/DavidF-Dev/FlingApp/releases/latest)
[![License](https://img.shields.io/github/license/DavidF-Dev/FlingApp?style=flat-square)](LICENSE)
[![CI](https://github.com/DavidF-Dev/FlingApp/actions/workflows/ci.yml/badge.svg)](https://github.com/DavidF-Dev/FlingApp/actions/workflows/ci.yml)

A lightweight tool for sending clipboard content from a Windows PC to an Android
phone over the local network. Take a screenshot, copy some text, or grab an image
— it arrives on your phone as a notification, ready to paste.

## How it works

The PC runs a CLI tool that sends content over HTTP to the phone. The phone runs a
foreground service with an embedded HTTP server that receives it. Pair once, then
it just works — auto-discovery finds your phone on any network without re-pairing.

This is a **clipboard tool**, not a file-sharing app. Content goes to the phone's
clipboard, not to storage. Notifications auto-expire. No cloud, no relay — local
network only.

## Components

| Component | Stack | Directory |
|-----------|-------|-----------|
| [CLI tool](cli/README.md) | .NET 8, C#, single-file exe | `cli/` |
| [Android app](android/README.md) | Kotlin, Jetpack Compose, Ktor | `android/` |

## Quick start

1. Install the Fling app on your Android phone and start the service.
2. On your PC, pair with the phone:

       fling pair --discover

3. Approve the pairing request on your phone.
4. Send content:

       fling send --clipboard --all
       fling send --image screenshot.png --all
       fling send --text "hello" --device "Pixel"

5. Tap the notification on your phone to copy to clipboard.

## Supported content

| Type | MIME | Notes |
|------|------|-------|
| Plain text | `text/plain` | GZip compressed in transit |
| Rich text | `text/html` | GZip compressed in transit |
| Image | `image/png` | Converted to PNG before sending |

## Protocol

HTTP POST from CLI to phone over local network. Default port: **7291**. Auth via
shared API key exchanged during pairing. Auto-discovery via UDP broadcast on port
**7290**. See [docs/DESIGN.md](docs/DESIGN.md) for the full protocol specification.

## License

[MIT](LICENSE) © 2026 David F Dev.
