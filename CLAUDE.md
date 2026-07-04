# Fling - Paste on Phone

A lightweight tool for sending clipboard content from a Windows PC to an Android phone over the local network.

## Project Structure

- `cli/` — Windows CLI tool (.NET 8, C#). Sends clipboard content or files to paired Android devices.
- `android/` — Android app (Kotlin, Jetpack Compose). Receives content and writes to phone clipboard on user tap.
- `docs/` — Design documents and protocol specification.

## Key Constraints

- This is a clipboard tool, not a file-sharing app. Content is sent to the phone's clipboard, not saved to storage.
- All communication is local network only (no cloud relay).
- Received content on the phone is transient — notification-based, auto-expires.
- The CLI tool is the primary interface on the PC side. A tray app may be added later.

## Tech Stack

| Component | Stack |
|-----------|-------|
| CLI | .NET 8, C#, single-file publish |
| Android | Kotlin, Jetpack Compose, Material 3, Ktor (embedded HTTP server), DataStore |

## Protocol

HTTP POST from CLI to Android over local network. See `docs/DESIGN.md` for full protocol specification.

Default port: 7291. Auth via shared API key exchanged during pairing.

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