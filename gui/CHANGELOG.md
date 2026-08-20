# Changelog

## [Unreleased]

### Added

- Tray icon with a menu: Fling…, Device manager, Settings, Quit. Double-clicking the icon opens the Fling window.
- Single instance per user session; launching again surfaces the running app.
- Device manager: paired devices with live reachability, devices found on the network, and pairing with a cancellable wait for approval. Devices can be entered by address where broadcast is blocked, and removed with confirmation.
- Paired devices update their stored address when discovery finds them elsewhere.
- Fling window: stages the clipboard when it opens, or a file via drag-drop, the picker, or Ctrl+V. Text and rich text can be edited before sending, images show a preview with their dimensions, and the payload size is shown against the configured limit. Enter sends, Escape closes.
- Content an app marked as private — a password manager entry, typically — is not staged automatically. Ctrl+V still sends it deliberately.
- Files that cannot be pasted on a phone are refused rather than sent as a file path.
- Sends report per-device outcomes, and the chosen device is remembered between sends.
- The Settings window is a placeholder pending Phase 4.
