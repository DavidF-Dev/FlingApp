# Fling Tray App

WPF tray application for Windows (`FlingTray.exe`). A peer front-end to the CLI, not a wrapper around it.

## Structure

- `src/Fling.Gui/` — the WPF project. References `Fling.Core` and `Fling.Windows` from `cli/src/`.
- `Fling.Gui.slnx` — solution covering this project plus the two shared libraries.

The shared libraries live under `cli/` for historical reasons; they are not CLI-specific. Referencing them across directories is deliberate — the tray app does not depend on the CLI, and neither executable requires the other at runtime.

`Fling.Tests` lives in the CLI solution and covers `Fling.Core`. Changing shared code from this solution will not run those tests locally; CI builds and tests both solutions.

## UI framework

This is the **only** project in the repository permitted to set `UseWPF` or `UseWindowsForms`. Either flag adds a framework reference to `Microsoft.WindowsDesktop.App`, which a self-contained publish ships whole and which cannot be trimmed. Adding one to a project the CLI references would cost it roughly 58 MB.

WPF drives the application. WinForms is present for exactly one type — `NotifyIcon` — because WPF has no notification-area equivalent. Enabling both makes several type names ambiguous; the aliases in `GlobalUsings.cs` resolve them in WPF's favour, so WinForms types are spelled out where genuinely wanted.

The tray app is not trimmed and is not ReadyToRun-constrained the way the CLI is; WPF is not trim-compatible.

## Conventions

- Keep the build at **0 warnings**.
- Set `ShutdownMode="OnExplicitShutdown"`. WPF's default exits the process when the last window closes, which for a tray app means dying the first time a user closes a dialog.
- Dispose the tray icon on exit. An undisposed icon lingers in the notification area until the user hovers over it.
- Reach the clipboard through Core's `IClipboardReader`, never `System.Windows.Forms.Clipboard`.
- Windows open through `WindowManager`, which keeps one live instance per type.

## History

Built in six phases, all shipped as `gui/v1.0.0`. `docs/gui_progress.md` records each one with the decisions that changed along the way and what remains deferred — global hotkey, clipboard watching, send history, two-way receive.
