# Fling - Paste on Phone

A lightweight tool for sending clipboard content from a Windows PC to an Android phone over the local network.

## Project Structure

- `cli/` — PC-side .NET solution. Contains the shared core libraries, the CLI tool, and the WPF tray app. See `cli/CLAUDE.md` for the project breakdown.
- `android/` — Android app (Kotlin, Jetpack Compose). Receives content and writes to phone clipboard on user tap.
- `docs/` — Design documents and protocol specification.

## Key Constraints

- This is a clipboard tool, not a file-sharing app. Content is sent to the phone's clipboard, not saved to storage.
- All communication is local network only (no cloud relay).
- Received content on the phone is transient — notification-based, auto-expires.
- The CLI and the tray app are peer front-ends over shared core libraries. The tray app does not wrap the CLI, and neither requires the other at runtime.
- Sending is always explicit. There is no clipboard watching or auto-send.

## Tech Stack

| Component | Stack |
|-----------|-------|
| Core | .NET 8 (`net8.0`), C#. No UI, no platform APIs. |
| CLI | .NET 8, C#, single-file publish |
| Tray app | .NET 8, WPF, single-file publish |
| Android | Kotlin, Jetpack Compose, Material 3, Ktor (embedded HTTP server), DataStore |

## Protocol

HTTP POST from PC to Android over local network. See `docs/DESIGN.md` for full protocol specification.

Default port: 7291. Auth via shared API key exchanged during pairing.

## Configuration

- `%APPDATA%\Fling\config.json` — devices and shared settings. Written by both front-ends; access is mutex-guarded and atomically written.
- `%APPDATA%\Fling\gui.json` — tray app preferences only. Never read or written by the core libraries or the CLI.

## Planning Docs

`docs/` holds live phase-by-phase plans: `cli_progress.md`, `android_progress.md`, `gui_progress.md`, `publish_progress.md`. Update the relevant plan when completing work it covers.

## Releases

Each component releases independently under a scoped tag — `cli/v*`, `android/v*`, `gui/v*` — with its own version, CHANGELOG, and release script. Versions are not aligned across components. Release notes cross-reference the other components' latest tags to express compatibility.

## Git

- Do not run git actions (commit, push, branch, reset, etc.) unless explicitly
  directed to. Staging, history, and remotes are managed by the user.

## Comments

- Write conservatively. Default to no comment; add one only when the WHY is non-obvious (a hidden constraint, a subtle invariant, a workaround for a specific bug).
- When a comment is warranted, keep `//` comments to a single concise line.
- Class and method `///` summaries may be multi-line.
- Describe what the target *is* / *does*; broader context belongs in external docs, not in code comments.
- Keep them self-contained and stable.
- **Comments shouldn't have dependencies. The acid test: a comment should not need to be edited unless the code immediately below it changes.**
- A reader who has never seen the rest of the codebase should be able to verify the comment against the local code alone.
- Do not refer to file paths or file names.