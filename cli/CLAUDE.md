# Fling CLI

Windows command-line tool that sends content to paired Android devices running the Fling app.

## Architecture

- .NET 8 console application, published as a single self-contained executable (trimmed, no runtime dependency).
- Uses `HttpClient` to POST content to the Android app's HTTP server.
- Configuration stored in `%APPDATA%\Fling\config.json`.
- Supports multiple paired devices with a default target.

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

## Integration

Designed to work with Greenshot's External Command Plugin:
```
fling send --image "{0}"
```

## Content Handling

- Text: sent as-is, GZip compressed at HTTP layer.
- Images: converted to PNG if needed, sent without additional compression.
- Max payload size: configurable (default 10MB).

## Conventions

- Keep the build at **0 warnings**.