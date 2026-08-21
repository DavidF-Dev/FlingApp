# Changelog

## [Unreleased]

## [1.0.0] - 2026-08-21

First public release.

A tray application for sending clipboard content to paired Android devices, alongside the existing command line tool. Neither requires the other; both share the same paired devices and settings.

### Sending

- Running the app opens the Fling window, whether that starts it or brings the running copy forward. Whatever you last copied is already staged, so the common case is launch, then Enter.
- Content can also come from drag-drop, a file picker, or Ctrl+V for something copied after the window opened.
- Text and rich text can be edited before sending. Images show a preview with their dimensions, and the payload size is shown against the configured limit.
- Sends report the outcome for each device, and remember which one you chose.
- Files that cannot be pasted on a phone are refused rather than sent as a file path.
- Content an app marked as private — a password manager entry, typically — is not staged automatically. Ctrl+V still sends it deliberately.

### Devices

- The Device manager lists paired devices with live reachability, and devices answering on the network.
- Pairing shows a cancellable wait for approval on the phone. Devices can be entered by address where broadcast is blocked, and removed with confirmation.
- A paired device that changes address is found again and its stored address updated.

### Notifications

- Send outcomes are reported through the notification area once the window has closed. Failures name the device and say whether it rejected the key or could not be reached; successes mark the tray icon briefly rather than interrupting.
- Configurable: always, only on failure (the default), or never.

### Settings

- Maximum size, compression, this PC's name, and logging are shared with the command line tool. Notification mode, remembering the last device, starting at sign-in, and the Explorer "Send to" entry belong to the tray app.
- Changes apply as they are made. Buttons open the config folder and the log file.

### Behaviour

- One instance per user session.
- Starting at sign-in launches into the tray without a window, and repoints itself if the app is later moved.
- Windows open on the display you are working on rather than always the primary one.
- With no devices paired, launching opens the Device manager, so a first run lands on pairing.
