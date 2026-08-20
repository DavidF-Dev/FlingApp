# GUI Tray App — Implementation Plan

This is a **live document** tracking the phased implementation of the Fling Windows tray application (`FlingTray.exe`). Each phase is a vertical slice that can be built, verified, and committed independently.

The tray app is a **second front-end** over the same logic the CLI uses. It does not wrap `fling.exe` — both executables consume a shared `Fling.Core` library. The CLI remains fully functional with the tray app closed; neither depends on the other at runtime.

Phase 0 is a prerequisite refactor of existing CLI code with no user-visible change. Phases 1–6 build the GUI.

---

## Scope

**In scope for v1:**

- Tray icon with a four-item menu: Fling…, Device manager, Settings, Quit.
- Fling window: stage content from clipboard, file picker, or drag-drop; preview it; send to one device or all.
- Device manager: paired device list with live reachability, plus pairing via UDP discovery.
- Settings: shared Fling settings and GUI preferences, run-at-startup, Explorer "Send to" toggle.
- Balloon-tip notifications for send results.

**Explicitly deferred** (see Deferred Features at the end): global hotkey binding, clipboard watching / auto-send, send history, two-way receive.

---

## Project Layout

Phase 0 splits the current single `Fling` project into four:

| Project | Target | Contents |
|---------|--------|----------|
| `Fling.Core` | `net8.0` | Config, protocol, content encoding, discovery, send orchestration. No UI, no platform APIs. |
| `Fling.Windows` | `net8.0-windows` | Windows-specific implementations: clipboard, image encoding, Explorer "Send to", startup registration. |
| `Fling` | `net8.0-windows` | Existing CLI. References Core + Windows. |
| `Fling.Gui` | `net8.0-windows` | WPF tray app. References Core + Windows. |

Four projects is more ceremony than a solo project usually wants, but each has one obvious job, and the `Core` / `Windows` split is what keeps a future non-Windows port from being a rewrite. If that port is ruled out permanently, `Fling.Windows` can be folded into `Fling.Core`.

---

## Phase 0: Extract `Fling.Core` and `Fling.Windows`

**Goal:** The CLI behaves identically, but its logic lives in libraries a second front-end can consume.

### Design decisions

- **`Fling.Core` targets `net8.0`, not `net8.0-windows`.** This is the whole point of the split — the compiler enforces that no Win32, COM, WinForms, or `System.Drawing` dependency leaks into shared logic.
- **Commands become thin adapters.** Each command in `Commands/` currently mixes orchestration with `Console` output. Orchestration moves to Core; the command keeps argument parsing, console writing, and exit-code mapping.
- **A `SendOperation` type in Core owns the send pipeline.** Encode → resolve devices → resolve addresses → send in parallel → apply name sync → return per-device results. Without this, the GUI reimplements the body of `SendCommand` and the two front-ends drift.
- **`SendOperation` returns results, it does not print them.** Per-device outcome (success, auth failure, network failure, resolved device name) is data. The CLI maps it to text and exit codes; the GUI maps it to a results list and a notification.
- **`ConfigStore` gets concurrency safety.** Today exactly one short-lived process touches `config.json`. A long-running tray app plus CLI invocations makes concurrent read-modify-write real. Use a cross-process named mutex around load-modify-save, and write via temp file + atomic replace so a crash mid-write cannot truncate the file holding the API keys.
- **`FlingConfig` preserves unknown JSON properties.** The CLI and tray app release independently and each bundles its own copy of Core, so a user can run an older CLI beside a newer tray app. `ConfigStore` deserializes and reserializes, which means an older build silently drops any field a newer build added. A `[JsonExtensionData]` dictionary makes that skew harmless instead of making it a release-discipline problem.
- **`IImageEncoder` joins `IClipboardReader` as a platform seam.** `ImageLoader` uses `System.Drawing` and cannot move to Core as-is.
- **The STA thread hop stays in the Windows clipboard implementation.** The CLI needs it; WPF's UI thread is already STA and does not. Keeping it inside the implementation means neither caller has to care.

### Tasks

- [ ] Create `Fling.Core` (`net8.0`). Move `Config/`, `Net/`, `Content/ClipPayload`, `Content/ContentEncoder`, `Content/FileContentResolver`, `Content/IClipboardReader`.
- [ ] Add `IImageEncoder` to Core; move `ImageLoader` to `Fling.Windows` as its implementation.
- [ ] Create `Fling.Windows` (`net8.0-windows`, `UseWindowsForms=true`). Move `WindowsClipboardReader`, the image encoder, and the COM shortcut code currently in `InstallCommand`/`UninstallCommand` into a reusable `SendToIntegration` type.
- [ ] Add `SendOperation` to Core, encapsulating the pipeline currently inlined in `SendCommand`.
- [ ] Add `PairOperation` to Core, encapsulating the pairing logic currently inlined in `PairCommand` (endpoint resolution, key generation, request, conflict detection, persistence).
- [ ] Add `ReachabilityProbe` to Core, encapsulating what `StatusCommand` does per device.
- [ ] Make `ConfigStore` concurrency-safe: named mutex + atomic write.
- [ ] Add `[JsonExtensionData]` to `FlingConfig` so unknown properties survive a load-modify-save by an older build.
- [ ] Rewrite the CLI commands as adapters over the new Core operations. Exit codes and all console output must be byte-identical to current behaviour.
- [ ] Update `Fling.slnx` and the publish/release scripts for the new project layout.

### Unit Tests

- [ ] Existing test suite passes unchanged against the refactored code. This is the primary safety net — no test should need editing except for namespace/project references.
- [ ] `SendOperation` returns per-device results for a mixed outcome (one success, one auth failure, one network failure) using the existing fake `HttpMessageHandler`.
- [ ] `SendOperation` applies phone-name sync to the config when a response carries a changed name.
- [ ] `ConfigStore` atomic write: a simulated failure mid-write leaves the previous file intact.
- [ ] `ConfigStore` concurrent save from two threads does not interleave or corrupt.
- [ ] A config file containing an unrecognised property survives a load-modify-save round trip with that property intact.

### Verification

1. `dotnet build` at 0 warnings across all four projects.
2. Every command in the CLI README produces identical output to the pre-refactor build.
3. `fling send --clipboard --all` against a real phone still works.
4. Full test suite green.

---

## Phase 1: Tray Shell

**Goal:** A tray icon with a working menu. Menu items open empty placeholder windows; Quit exits.

### Design decisions

- **`ShutdownMode.OnExplicitShutdown`.** WPF's default terminates the app when the last window closes, which for a tray app means it dies the first time a user closes a dialog. This must be set before any window is shown.
- **Tray icon via WinForms `NotifyIcon`.** Enable `UseWPF` and `UseWindowsForms` together and use `NotifyIcon` directly rather than adding a third-party tray package. It also gives us `ShowBalloonTip` for free in Phase 5.
- **Single instance via a named mutex.** A second launch signals the running instance to open the Fling window, then exits.
- **`FlingTray.exe`, not `Fling.exe`.** `dist/` already contains `fling.exe` and `flingw.exe`; a third binary claiming a similar name in the same folder is a support problem.
- **The tray app never shells out to the CLI.** Both are front-ends over Core.

### Tasks

- [ ] Create `Fling.Gui` (WPF, `net8.0-windows`, `UseWindowsForms=true`), referencing Core and Windows.
- [ ] Application bootstrap: `ShutdownMode.OnExplicitShutdown`, no `StartupUri`, single-instance mutex.
- [ ] `TrayIconHost`: creates the `NotifyIcon`, owns the context menu, disposes cleanly on exit (an undisposed tray icon leaves a ghost until the user hovers it).
- [ ] Context menu: Fling…, Device manager, Settings, separator, Quit.
- [ ] Double-click on the tray icon opens the Fling window.
- [ ] Window manager helper: one live instance per window type; re-invoking a menu item focuses the existing window rather than opening a second.
- [ ] Reuse `app.ico` for the tray and window icons.

### Verification

1. App launches with no window shown; icon appears in the tray.
2. Each menu item opens its placeholder window; re-clicking focuses rather than duplicating.
3. Closing every window leaves the app running in the tray.
4. Quit exits the process and removes the icon immediately.
5. Launching a second instance focuses the first and exits.

---

## Phase 2: Device Manager Window

**Goal:** View paired devices with live reachability, and pair new ones.

### Design decisions

- **Pairing and device management share one window.** Pairing is not frequent enough to deserve its own top-level entry, and a discovered-but-unpaired device belongs visually next to the paired ones.
- **Pairing is a state machine, not a blocking call.** `POST /pair` waits on the user tapping Accept on the phone (60s client timeout). States: *idle → discovering → pairing (cancellable) → accepted / rejected / timed out*. The UI thread must never block on any of them.
- **Discovery loops while the window is open.** `UdpDiscovery` is a one-shot 1.5s broadcast returning a snapshot. A GUI that broadcasts once shows an empty list to anyone whose phone was asleep. Re-broadcast on an interval and merge results, so devices appear as they wake.
- **Reachability polling runs only while this window is open.** A tray app pinging a phone all day is a battery complaint. No background polling in v1.
- **No local rename.** Device names sync passively from the phone — a `/clip` or `/ping` response carrying a different name overwrites the stored one. A name typed on the PC would be silently clobbered on the next send. Renaming happens on the phone; this window links to that instead.
- **Removal is confirmed and one-sided.** Removing discards the API key and requires re-pairing. The phone keeps its own stale entry until cleared there. The confirmation dialog says both things.

### Tasks

- [ ] `DeviceManagerWindow` with two sections: paired devices, and devices found on the network.
- [ ] Paired list rows: name, `host:port`, reachability indicator, last-seen, Remove.
- [ ] Reachability polling on an interval while the window is open, via Core's `ReachabilityProbe`. Cancelled on close.
- [ ] Repeating discovery loop while the window is open; merge results and exclude already-paired devices.
- [ ] Pair flow: click a discovered device → confirm PC name → "Waiting for approval on <device>…" with Cancel → success, rejection, or timeout, each with a distinct message.
- [ ] Manual pairing fallback: enter `ip:port` directly, for when broadcast is blocked by the network.
- [ ] Remove with a confirmation dialog stating that re-pairing is required and the phone entry must be cleared separately.
- [ ] Empty state when no devices are paired: explain what pairing is and point at the phone app.

### Unit Tests

- [ ] Discovery result merging: the same device seen across successive broadcasts appears once; a device that stops responding is marked stale rather than vanishing mid-interaction.
- [ ] Already-paired devices are excluded from the discovered list.
- [ ] Pairing view-model transitions cover accepted, rejected, timed out, and user-cancelled.

### Verification

1. A paired, powered-on phone shows as reachable; airplane mode flips it to unreachable within one poll interval.
2. Pairing a fresh device end-to-end: discovered → tap Accept on phone → appears in the paired list.
3. Rejecting on the phone shows a rejection message and stores nothing.
4. Closing the window mid-pair cancels cleanly with no orphaned request or config write.
5. Removing a device is reflected in `fling config show`.

---

## Phase 3: Fling Window

**Goal:** The core interaction — stage something, look at it, send it.

### Design decisions

- **The clipboard is auto-staged when the window opens.** "I copied something, now send it" is the dominant case; requiring a paste click afterwards is a step that exists only because other sources exist. Ctrl+V and the Paste button re-read the clipboard for content copied after the window opened.
- **Sensitive clipboard content is not staged.** Password managers set the `ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory` clipboard formats. When either is present, stage nothing and show an explanatory empty state. Nothing is transmitted without an explicit Fling press regardless, but rendering a vault entry on screen is its own problem.
- **Unsupported file types are rejected, not silently reinterpreted.** `FileContentResolver` falls back to sending the *file path as text* for binary files. That backstops `Send to > Fling` in Explorer, where the alternative is nothing at all. In a window with a Fling button and a preview, it reads as file transfer and delivers a useless string. The GUI rejects those types with a message naming the actual constraint: Fling sends things that can be pasted. The fallback stays CLI-only.
- **Preview is mandatory.** The CLI has `--dry-run` for this reason. Always show resolved type, size, and either a thumbnail or the text itself, before anything is sent. Sending stale clipboard content is the failure mode this exists to prevent.
- **Staged text is editable.** A `TextBox` instead of a label costs nothing and covers trimming a URL or fixing a typo.
- **"Send as plain text" toggle for rich text.** `WindowsClipboardReader` prefers `text/html` over plain text, so anything copied from a browser arrives as HTML. The toggle appears only when the staged content is HTML. Its default is remembered in `gui.json`.
- **Device selector defaults to All when more than one device is paired.** This departs from the CLI's deliberate no-default rule, which exists to prevent silently broadcasting sensitive content. The GUI's mandatory preview and explicit confirm remove that failure mode. With exactly one paired device, the selector shows that device, not "All".
- **Zero paired devices short-circuits.** Opening the Fling window with nothing paired opens the Device manager instead of a window that cannot do anything.

### Tasks

- [ ] `FlingWindow` with staging area, preview, device selector, and Fling button.
- [ ] Staging model with four sources: auto-staged clipboard on open, Paste button / Ctrl+V, file picker, drag-drop.
- [ ] Sensitive-clipboard-format detection; empty state when detected.
- [ ] Preview: thumbnail with pixel dimensions for images; editable text box with character count for text; resolved content type and payload size for both.
- [ ] Reject unsupported file types with a message explaining the clipboard-tool constraint.
- [ ] Warn before sending when the encoded payload exceeds `maxSizeMb`, naming the current limit.
- [ ] Device selector: All (when >1 paired) plus each device. Optionally remembers the last selection, per `gui.json`.
- [ ] Send with progress and Cancel. A 10MB PNG over Wi-Fi is not instant and currently has no feedback anywhere.
- [ ] Per-device result display. `SendOperation` returns per-device outcomes; a partial failure must name which device failed and why.
- [ ] Keyboard: Enter sends, Esc closes, Ctrl+V pastes.
- [ ] Window closes on full success; stays open showing results on partial or total failure.
- [ ] Open near the cursor, on the monitor the cursor is on.

### Unit Tests

- [ ] Staging replaces rather than accumulates: file picker after clipboard leaves exactly one staged item.
- [ ] Content classification matches `FileContentResolver` for image, text, and rejected-binary cases.
- [ ] "Send as plain text" converts a staged HTML item to `text/plain`.
- [ ] Oversized payload is flagged before any network call.
- [ ] Send view-model surfaces mixed per-device results correctly.

### Verification

1. Copy an image → open Fling → thumbnail is already staged → Enter → arrives on phone.
2. Copy from a web page → preview shows rich text with the plain-text toggle available.
3. Drag a `.png` on → replaces staged content; drag a `.pdf` on → clear rejection, nothing sent.
4. Copy a password-manager entry → nothing staged, explanation shown.
5. Send to All with one device offline → success and failure both named.
6. Cancel a large send mid-flight → no partial write, window stays usable.

---

## Phase 4: Settings Window

**Goal:** Configure both shared Fling settings and GUI-only preferences, with the distinction visible.

### Design decisions

- **GUI preferences live in `%APPDATA%\Fling\gui.json`, separate from `config.json`.** See the Decisions Log in DESIGN.md. In short: GUI preferences change on nearly every interaction while `config.json` changes rarely, and `config.json` holds the API keys. Keeping the chatty writes out of the precious file avoids both the contention and the blast radius.
- **The window groups settings by scope.** "Fling settings" (shared with `fling.exe`) and "App preferences" (tray app only) are separate headed groups, so a user changing `maxSizeMb` understands it affects the CLI too.
- **`runAtStartup` is not stored in either file.** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` is the source of truth and is read live when the window opens. A cached copy lies as soon as someone disables the entry from Task Manager's Startup tab. Registry over a Startup-folder shortcut: user-scope, no admin, no COM, and it is what Task Manager actually controls.
- **A startup launch comes up minimized to tray**, with no window, via a command-line flag on the registered command.
- **Explorer "Send to" is exposed as a checkbox.** It calls the same Core code as `fling install` / `fling uninstall`. Most users would never discover it as a CLI command.

### Tasks

- [ ] `SettingsWindow` with two headed groups.
- [ ] Fling settings (write through Core's `ConfigStore`): max payload size, compression, PC name with the machine-name default shown as a hint, file logging.
- [ ] App preferences (`gui.json`): notification mode, remember-last-device, default plain-text-for-HTML.
- [ ] Run at startup: read/write `HKCU\...\Run`, re-read on window open, register with a start-minimized flag.
- [ ] Explorer "Send to" checkbox over the shared `SendToIntegration`.
- [ ] "Open log file" button, enabled only when logging is on, plus a button to open the config folder.
- [ ] About: version, license, project link.
- [ ] `GuiSettingsStore` with the same defensive posture as `ConfigStore`: missing file returns defaults, corrupt file falls back to defaults rather than throwing — GUI preferences are not worth blocking startup over.

### Unit Tests

- [ ] `GuiSettingsStore` round-trips; missing file yields defaults; corrupt file yields defaults without throwing.
- [ ] Startup registration writes and removes the expected registry value, and reports state accurately when the value is absent or points elsewhere.
- [ ] Shared settings written by the GUI are read back by Core's `ConfigStore` unchanged.

### Verification

1. Change max size in the GUI → `fling config show` reflects it.
2. Change it via `fling config set` → reopening Settings shows the new value.
3. Toggle run-at-startup → registry entry appears; disable it in Task Manager → Settings reflects that on next open.
4. Toggle Send to → the Explorer context menu entry appears and disappears.
5. Deleting `gui.json` while running does not crash the app.

---

## Phase 5: Notifications & Tray Polish

**Goal:** Sensible feedback for sends, without becoming noise.

### Design decisions

- **Balloon tips via `NotifyIcon.ShowBalloonTip`, not the Windows toast APIs.** Proper toasts from an unpackaged .NET app require an AppUserModelID and a Start Menu shortcut before they render at all. On Windows 10/11 a balloon tip is surfaced as a real toast anyway. Revisit only if notification action buttons become necessary.
- **Failure-only is the default.** A success toast per send is noise on a tool used twenty times a day. Three modes: Always, Failures only, Never. Success at the default setting is signalled by a brief tray icon change instead.
- **Notifications fire for sends started from any window**, so the user can send and immediately move on.

### Tasks

- [ ] Notification service over `ShowBalloonTip`, honouring the configured mode.
- [ ] Failure notifications name the device and the reason, distinguishing auth failure from unreachable — the CLI already separates these as exit codes 3 and 2.
- [ ] Brief tray icon state change on success.
- [ ] Tray tooltip shows paired device count.
- [ ] `--minimized` startup flag suppresses any window on launch.
- [ ] Verify tray icon behaviour across DPI changes and Explorer restart (a `NotifyIcon` is lost when Explorer restarts unless re-registered).

### Verification

1. Failed send → notification names the device and reason. Successful send at default settings → no notification.
2. Set to Always → success notifications appear; set to Never → neither appears.
3. Launch with `--minimized` → no window.
4. Restart Explorer → tray icon returns.
5. Move the window between monitors with different scaling → no blurring or layout breakage.

---

## Phase 6: Packaging & Distribution

**Goal:** A shippable tray app alongside the existing CLI.

### Design decisions

- **The tray app releases independently under a `gui/vX.Y.Z` tag.** The repo already releases per component — `cli/v1.0.0`, `android/v1.0.0`, `android/v1.0.1` — with Android running ahead of the CLI. The release axis is the shipped artifact, not the source directory, so the tray app living inside the PC-side solution does not make it part of the CLI's release.
- **Independent version numbers, not a shared one.** The CLI is stable; the tray app has six phases of churn ahead. A shared version means either republishing byte-identical CLI binaries under bumped versions or holding tray releases behind CLI ones. Compatibility is expressed the way the existing scripts already express it: a cross-reference line in the release notes naming the latest tag of the other components.
- **Its own zip, containing only the tray app.** `fling.exe` and `flingw.exe` are each a full self-contained runtime — 69 MB apiece, zipping to 127 MB together — so bundling everything into one download would approach 200 MB to serve two audiences that mostly want one binary each. The release notes link to the CLI release instead.
- **No PE subsystem patching.** The two-exe `fling.exe`/`flingw.exe` trick exists because a console app needs both a console and a no-console variant. A WPF app is GUI-subsystem natively.
- **Config compatibility across version skew is handled in code, not by release discipline.** See the `[JsonExtensionData]` decision in Phase 0. Users will run mismatched front-end versions and neither release process can prevent it.

### Tasks

- [ ] Extend `publish.ps1` to produce `FlingTray.exe` (self-contained, single-file, compressed) and its own zip.
- [ ] Add `gui/scripts/release.ps1`, or parameterise the existing script by tag prefix, csproj path, and CHANGELOG path. Three near-identical copies is the point at which extraction pays for itself.
- [ ] Cross-reference the latest `cli/v*` and `android/v*` tags in the tray app's release notes, matching the existing convention.
- [ ] Separate `CHANGELOG.md` for the tray app, versioned independently.
- [ ] `packaging/README.txt` covers first-run: launch, pair, fling.
- [ ] Root `README.md` and `cli/README.md` describe both front-ends, state that neither requires the other, and link both releases.
- [ ] Rename the CI job from "CLI (build & test)" to cover the whole PC-side solution. It already builds `cli/Fling.slnx`, so the new projects are picked up with no workflow change.
- [ ] Smoke test on a clean Windows machine with no .NET runtime installed.

### Verification

1. Clean-machine launch works with no runtime installed.
2. Pair → fling clipboard → arrives on phone, using only the tray app.
3. CLI still works with the tray app closed, and with it running.
4. Both processes running simultaneously do not corrupt `config.json`.
5. An older CLI build and a newer tray build share `config.json` without either dropping the other's fields.

---

## Deferred Features

Not in v1. Listed so the v1 architecture does not accidentally foreclose them.

| Feature | Why deferred | What v1 should not break |
|---------|--------------|--------------------------|
| Global hotkey binding | The Fling window with auto-staged clipboard already collapses the common path to open-and-Enter. Hotkey registration and conflict handling is meaningful work for a marginal gain on top of that. | Keep the send path callable without a window. |
| Clipboard watching / auto-send | Largest safety surface in the whole idea. Needs armed-mode UI, unmistakable state indication, sensitive-format handling, and self-send suppression. Warrants its own phase. | The sensitive-format detection built in Phase 3 is the foundation. |
| Send history with re-send | The phone side is deliberately transient; a PC-side history is a new concept with its own retention and privacy questions. | Keep `SendOperation` results as structured data. |
| Two-way receive (phone → PC) | Needs a listener on the PC, a new protocol direction, and a firewall prompt. Its own project. | Nothing in v1 assumes the PC is send-only at the protocol layer. |
| Cross-platform GUI | No demand established; Linux clipboard access is genuinely difficult and macOS has Universal Clipboard. | `Fling.Core` stays at `net8.0` with platform code behind interfaces. |
