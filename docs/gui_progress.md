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

| Project | Location | Target | UI framework | Contents |
|---------|----------|--------|--------------|----------|
| `Fling.Core` | `cli/src/` | `net8.0` | none | Config, protocol, content encoding, discovery, send orchestration. No UI, no platform APIs. |
| `Fling.Windows` | `cli/src/` | `net8.0-windows` | **none** | Windows implementations behind Core's interfaces: clipboard (Win32 P/Invoke), image encoding, Explorer "Send to", startup registration. |
| `Fling` | `cli/src/` | `net8.0-windows` | **none** | CLI. References Core + Windows. |
| `Fling.Gui` | `gui/src/` | `net8.0-windows` | WPF + WinForms | Tray app. References Core + Windows. |

`Fling.Core` and `Fling.Windows` sit under `cli/` for historical reasons and are shared, not CLI-specific — `gui/` references them across directories deliberately. The tray app does not depend on the CLI.

Each front-end has its own solution: `cli/Fling.slnx` (Core, Windows, CLI, tests) and `gui/Fling.Gui.slnx` (Core, Windows, tray app, tests). Core and Windows belong to both. CI builds and tests both; a Core change made from the GUI solution will not run the CLI-side tests locally, so CI is the backstop there.

Four projects is more ceremony than a solo project usually wants, but each has one obvious job, and the `Core` / `Windows` split is what keeps a future non-Windows port from being a rewrite.

**No project except `Fling.Gui` may set `UseWindowsForms` or `UseWPF`.** These flags add a `FrameworkReference` to `Microsoft.WindowsDesktop.App`, and a self-contained publish ships that runtime pack whole — including WPF — regardless of what is actually called. They also make the project untrimmable. A single `UseWindowsForms=true` anywhere in the CLI's reference graph costs it roughly 58 MB and its ability to trim. See Phase 0.

The `-windows` TFM itself is free: it enables Windows-only BCL surface (registry, platform-guard analyzers) without pulling the desktop runtime pack. Measured identical to plain `net8.0`.

---

## Phase 0: Extract `Fling.Core`, Slim the CLI ✅

**Shipped as `cli/v1.0.1`.**

**Goal:** The CLI behaves identically, but its logic lives in libraries a second front-end can consume — and it stops shipping a desktop UI framework it never calls.

### The size problem this phase fixes

The CLI currently sets `UseWindowsForms=true` for exactly two APIs: `System.Windows.Forms.Clipboard` (seven call sites in one file) and `System.Drawing.Image` (two lines). That flag adds the `Microsoft.WindowsDesktop.App` framework reference, and a self-contained publish ships the pack whole. Measured from the unpacked publish output:

| Shipping in `fling.exe` | Size | Called by the CLI |
|---|---|---|
| `PresentationFramework.dll` | 16 MB | no — WPF |
| `PresentationCore.dll` | 8.2 MB | no — WPF |
| `D3DCompiler_47_cor3.dll` | 4.6 MB | no — WPF rendering |
| `WindowsBase.dll` | 2.2 MB | no — WPF |
| `System.Windows.Forms.Design.dll` | 5.4 MB | no — designer |
| `System.Windows.Forms.dll` | 13 MB | one class |
| `System.Windows.Forms.Primitives.dll` | 2.9 MB | transitively |

WinForms and WPF are also not trim-compatible, so `PublishTrimmed` is unavailable while either is referenced. Measured outcomes for `fling --version`, warm start averaged over five runs:

| Configuration | Size | Cold start | Warm start |
|---|---|---|---|
| Before (WinForms, untrimmed) | 69.0 MB | 1445 ms | 204 ms |
| Trimmed, no ReadyToRun | 11.6 MB | 734 ms | 284 ms |
| Trimmed, uncompressed, R2R | 24.5 MB | 1707 ms | 110 ms |
| **Trimmed, compressed, R2R** | **15.9 MB** | **618 ms** | **151 ms** |

Startup matters independently of size: single-file plus compression self-extracts to `%LOCALAPPDATA%\Temp\.net\`, and the CLI's primary callers — Greenshot's External Command Plugin and Explorer "Send to" — are exactly where a stall reads as a failed send. Warm start is the case that recurs; cold start is paid once per update.

The middle row is the trap. Trimming alone makes the CLI *slower to start* than it was, because the trimmer rewrites the framework assemblies and discards the precompiled ReadyToRun code Microsoft ships them with, leaving everything to the JIT. Trimming and ReadyToRun have to be enabled together; the last row beats the original on all three measures and is what ships.

This work belongs in Phase 0 rather than a later pass because both offending files are being relocated behind interfaces here anyway. Rewriting the clipboard reader while it moves is far cheaper than moving it twice.

### Design decisions

- **`Fling.Core` targets `net8.0`, not `net8.0-windows`.** This is the whole point of the split — the compiler enforces that no Win32, COM, WinForms, or `System.Drawing` dependency leaks into shared logic.
- **`Fling.Windows` sets no UI framework flag.** It is `net8.0-windows` with `UseWindowsForms` absent. If it referenced WinForms, the CLI would inherit the desktop runtime pack transitively and none of the size or trimming gains would materialise. WinForms exists in this solution for one reason — `NotifyIcon` in the tray app — and must not spread beyond `Fling.Gui`.
- **Clipboard reading moves to Win32 P/Invoke.** `OpenClipboard` / `GetClipboardData` with `CF_UNICODETEXT`, `CF_DIB`, and the registered `HTML Format`. The existing `ExtractHtmlFragment` parses the raw CF_HTML string and carries over unchanged. This is the single largest task in the phase.
- **`System.Drawing` stays, as a package reference.** It never required the desktop runtime pack — `System.Drawing.Common` is a standalone NuGet package over GDI+, and it arrived free as a side effect of `UseWindowsForms` being set for `Clipboard`. Measured at 10.2 MB trimmed with zero trim warnings, so `ImageLoader` needs no change and image handling keeps bit-identical format support. ImageSharp and WIC were both considered and rejected: neither can guarantee unchanged behaviour, WIC has no managed wrapper outside `PresentationCore`, and GDI+ is serviced by Windows Update rather than requiring a Fling release to patch an image-decoder CVE. Being Windows-only is not a constraint here — this lives in `Fling.Windows`.
- **PNG input passes through without re-encoding.** `LoadAsPng` currently decodes every image to a bitmap and re-encodes it, including files that are already PNG. That is the Greenshot path — screenshot to PNG, then `send --image` — so the common case pays a full decode/encode round trip for nothing, and GDI+ does not preserve the source's compression settings, so the output is frequently *larger* than the input and therefore slower to transfer.
- **The pass-through is gated on content, not the file extension.** A file named `.png` that is not a PNG must not reach the phone unvalidated. Check the 8-byte signature, and also confirm the file ends with a well-formed `IEND` chunk: without that second check a truncated PNG stops failing fast on the PC — as it does today — and instead sends a broken image the user only discovers on the phone. Truncation is the dominant corruption mode and the check costs one seek.
- **`PublishTrimmed` is enabled for the CLI, not the tray app.** WPF is not trim-compatible, so `Fling.Gui` stays untrimmed. The two front-ends do not need the same publish settings.
- **Trim warnings are build failures, not advisories.** `TreatWarningsAsErrors=true` is already set, and trimming raises IL2026 on reflection-based `JsonSerializer.Serialize<T>`. Source-generated `JsonSerializerContext` for `FlingConfig` and the protocol DTOs is therefore mandatory, not optional. It is also a prerequisite if NativeAOT is ever pursued.
- **`PublishReadyToRun` is mandatory alongside trimming**, not an optional extra. See the table above: trimming without it regresses warm start from 204 ms to 284 ms. It costs 4.3 MB and returns 133 ms per invocation.
- **The STA thread hop is no longer needed.** The previous reader spun an STA thread because the WinForms `Clipboard` wrapper goes through OLE. The raw Win32 clipboard functions carry no apartment requirement, so the reader calls them directly.
- **The Explorer shortcut is written through `IShellLink`, not `WScript.Shell`.** The scripting object is reached by ProgID and invoked through IDispatch; the trim analyzer flagged both (IL2072, then IL2050), and it is correct to — trimming can discard the metadata that late binding and COM vtable dispatch depend on. Declaring the interfaces with `[GeneratedComInterface]` moves the marshalling to compile time and removes the warning rather than suppressing it.
- **NativeAOT is not attempted here.** It would reach roughly 5–8 MB with near-zero startup, but requires the MSVC "Desktop Development for C++" workload on every build machine. Trimming plus ReadyToRun captures most of the benefit with no toolchain prerequisite. The source-generated JSON and COM interop this phase introduces are the two prerequisites, so the option stays open.
- **Commands become thin adapters.** Each command in `Commands/` currently mixes orchestration with `Console` output. Orchestration moves to Core; the command keeps argument parsing, console writing, and exit-code mapping.
- **A `SendOperation` type in Core owns the send pipeline.** Encode → resolve devices → resolve addresses → send in parallel → apply name sync → return per-device results. Without this, the GUI reimplements the body of `SendCommand` and the two front-ends drift.
- **`SendOperation` returns results, it does not print them.** Per-device outcome (success, auth failure, network failure, resolved device name) is data. The CLI maps it to text and exit codes; the GUI maps it to a results list and a notification.
- **`ConfigStore` gets concurrency safety.** Today exactly one short-lived process touches `config.json`. A long-running tray app plus CLI invocations makes concurrent read-modify-write real. Use a cross-process named mutex around load-modify-save, and write via temp file + atomic replace so a crash mid-write cannot truncate the file holding the API keys.
- **`FlingConfig` preserves unknown JSON properties.** The CLI and tray app release independently and each bundles its own copy of Core, so a user can run an older CLI beside a newer tray app. `ConfigStore` deserializes and reserializes, which means an older build silently drops any field a newer build added. A `[JsonExtensionData]` dictionary makes that skew harmless instead of making it a release-discipline problem.
- **`IImageEncoder` joins `IClipboardReader` as a platform seam.** `ImageLoader` uses `System.Drawing` and cannot move to Core as-is.
- **The STA thread hop stays in the Windows clipboard implementation.** The CLI needs it; WPF's UI thread is already STA and does not. Keeping it inside the implementation means neither caller has to care.

### Tasks

**Restructuring**

- [x] Create `Fling.Core` (`net8.0`). Move `Config/`, `Net/`, `Content/ClipPayload`, `Content/ContentEncoder`, `Content/FileContentResolver`, `Content/IClipboardReader`.
- [x] Add `IImageEncoder` to Core; `ImageLoader` becomes its Windows implementation.
- [x] Create `Fling.Windows` (`net8.0-windows`, **no** `UseWindowsForms` / `UseWPF`). Home for the clipboard reader, image encoder, a reusable `SendToIntegration` extracted from `InstallCommand`/`UninstallCommand`, and startup registration for Phase 4.
- [x] Add `SendOperation` to Core, encapsulating the pipeline currently inlined in `SendCommand`.
- [x] Add `PairOperation` to Core, encapsulating the pairing logic currently inlined in `PairCommand` (endpoint resolution, key generation, request, conflict detection, persistence).
- [x] Add `ReachabilityProbe` to Core, encapsulating what `StatusCommand` does per device.
- [x] Make `ConfigStore` concurrency-safe: named mutex + atomic write.
- [x] Add `[JsonExtensionData]` to `FlingConfig` so unknown properties survive a load-modify-save by an older build.
- [x] Rewrite the CLI commands as adapters over the new Core operations. Exit codes and all console output must be byte-identical to current behaviour.
- [x] Update `Fling.slnx` and the publish/release scripts for the new project layout.

**Slimming**

- [x] Replace `System.Windows.Forms.Clipboard` with a Win32 P/Invoke reader. Preserve current format precedence exactly: image before HTML before plain text. Carry `ExtractHtmlFragment` over unchanged.
- [x] Swap `UseWindowsForms` for a `System.Drawing.Common` package reference. `ImageLoader` itself is unchanged.
- [x] Add a CF_DIB → PNG path for the clipboard reader: prepend a `BITMAPFILEHEADER` to the DIB and decode via `Image.FromStream`, then encode PNG through the same `IImageEncoder`.
- [x] Add the PNG pass-through to `LoadAsPng`: validate the signature and trailing `IEND` chunk, and on a match return the file bytes verbatim. Files shorter than the signature must fall through to the decoder rather than throwing, and the existing missing-file check stays first.
- [x] Confirm no `Microsoft.WindowsDesktop.App` framework reference remains anywhere in the CLI's graph.
- [x] Add source-generated `JsonSerializerContext` for `FlingConfig` and the protocol DTOs; switch `ConfigStore` and `FlingHttpClient` to the generated overloads.
- [x] Enable `PublishTrimmed` for the CLI and resolve every trim warning. `TreatWarningsAsErrors=true` means none can be left outstanding.
- [x] Update `publish.ps1` for the trimmed CLI build. The PE subsystem patch producing `flingw.exe` is unaffected — verify the subsystem byte is still at the expected offset in the trimmed output.

### Unit Tests

- [x] Existing test suite passes unchanged against the refactored code. This is the primary safety net — no test should need editing except for namespace/project references.
- [x] `SendOperation` returns per-device results for a mixed outcome (one success, one auth failure, one network failure) using the existing fake `HttpMessageHandler`.
- [x] `SendOperation` applies phone-name sync to the config when a response carries a changed name.
- [x] `ConfigStore` atomic write: a simulated failure mid-write leaves the previous file intact.
- [x] `ConfigStore` concurrent save from two threads does not interleave or corrupt.
- [x] A config file containing an unrecognised property survives a load-modify-save round trip with that property intact.
- [x] Clipboard reader format precedence is unchanged: an item carrying both image and text yields the image; one carrying both HTML and plain text yields HTML.
- [x] `ExtractHtmlFragment` tests pass unmodified against the P/Invoke reader's CF_HTML output.
- [x] Image encoder converts JPG, BMP, and GIF to PNG, and rejects a missing file with the same exception type as before. These tests should pass unmodified — `ImageLoader` is not being rewritten.
- [x] A CF_DIB blob captured from the clipboard decodes to a PNG with correct dimensions and no vertical flip (DIBs are bottom-up by default).
- [x] A valid PNG is returned byte-for-byte identical to the file on disk.
- [x] A JPEG renamed to `.png` is decoded and re-encoded, not passed through.
- [x] A PNG truncated mid-file is rejected with the same error as any other unreadable image, not passed through.
- [x] A file shorter than the PNG signature produces the decoder's error, not an indexing exception.
- [x] Config and protocol DTOs round-trip through the source-generated serializer identically to the reflection-based one.

### Verification

1. ✅ `dotnet build` at 0 warnings, 0 errors across all four projects.
2. ✅ Output diffed command by command against the pre-refactor `fling.exe`: identical for every command and every error path, including exit codes. The only deltas were the embedded git hash in `--version` and the usage line's executable name, which System.CommandLine derives from the filename.
3. ✅ Verified against a physical phone on the local network.
4. ✅ Full suite green: 159 tests, up from 130. All 130 pre-existing tests pass unmodified.
5. ✅ Published `fling.exe` is **15.9 MB**, from 69.0 MB. The threshold is now ≤ 18 MB rather than ≤ 12 MB: ReadyToRun costs 4.3 MB and is what keeps startup ahead of the original build. `publish.ps1` fails the build above 18 MB.
6. ✅ No `PresentationFramework.dll`, `System.Windows.Forms.dll`, or `D3DCompiler_47_cor3.dll` in the publish output.
7. ✅ Cold start **618 ms**, from 1445 ms. Warm start **151 ms**, from 204 ms.
8. ✅ Every command exercised against the trimmed binary, including the two paths most at risk: the Win32 clipboard reader (read rich text from a live clipboard) and the COM shell link (`fling install` wrote a valid 984-byte shortcut, byte-size identical to the WScript.Shell original). `flingw.exe` runs correctly after the PE subsystem patch.

### Findings

- **Fixed a pre-existing bug: `fling send --all` failed for two or more devices.** `FlingHttpClient` assigned `HttpClient.Timeout` per call, but that property can only be set before the first request. All commands share one client across parallel sends, so the second device threw `InvalidOperationException`. Per-operation timeouts now come from a linked `CancellationTokenSource`. The new multi-device test is what surfaced it; nothing in the old suite covered more than one device.
- **Read-modify-write on `config.json` could discard a concurrent change.** Every caller loaded the config, mutated it, and wrote the whole file back, so a device paired by another process mid-command would be erased. `ConfigStore.Update` now holds the lock across load, mutate, and save, and the name-sync and address-sync paths re-match by device name against a freshly loaded config.
- **The trim analyzer earned its keep.** It rejected `WScript.Shell` twice (IL2072 on ProgID activation, IL2050 on COM marshalling) before either could fail at runtime in a user's hands.

---

## Phase 1: Tray Shell ✅

**Goal:** A tray icon with a working menu. Menu items open empty placeholder windows; Quit exits.

### Design decisions

- **`ShutdownMode.OnExplicitShutdown`.** WPF's default terminates the app when the last window closes, which for a tray app means it dies the first time a user closes a dialog. This must be set before any window is shown.
- **Tray icon via WinForms `NotifyIcon`.** Enable `UseWPF` and `UseWindowsForms` together and use `NotifyIcon` directly rather than adding a third-party tray package. It also gives us `ShowBalloonTip` for free in Phase 5. `Fling.Gui` is the **only** project permitted to set either flag — see Phase 0 for what happens when WinForms reaches the CLI. Clipboard access here goes through Core's `IClipboardReader`, not `System.Windows.Forms.Clipboard`.
- **`Fling.Gui` is not trimmed.** WPF is not trim-compatible. The tray app accepts the larger binary; the CLI does not have to.
- **Single instance via a named mutex.** A second launch signals the running instance to open the Fling window, then exits.
- **`FlingTray.exe`, not `Fling.exe`.** `dist/` already contains `fling.exe` and `flingw.exe`; a third binary claiming a similar name in the same folder is a support problem.
- **The tray app never shells out to the CLI.** Both are front-ends over Core.

### Tasks

- [x] Create `Fling.Gui` (WPF, `net8.0-windows`, `UseWindowsForms=true`), referencing Core and Windows.
- [x] Application bootstrap: `ShutdownMode.OnExplicitShutdown`, no `StartupUri`, single-instance mutex.
- [x] `TrayIconHost`: creates the `NotifyIcon`, owns the context menu, disposes cleanly on exit (an undisposed tray icon leaves a ghost until the user hovers it).
- [x] Context menu: Fling…, Device manager, Settings, separator, Quit.
- [x] Double-click on the tray icon opens the Fling window.
- [x] Window manager helper: one live instance per window type; re-invoking a menu item focuses the existing window rather than opening a second.
- [x] Reuse `app.ico` for the tray and window icons.

### Verification

1. ✅ Launches with no window shown and stays resident.
2. ⏳ Needs a human click — each menu item opening its window, and re-clicking focusing rather than duplicating.
3. ✅ `WM_CLOSE` to the Fling window left the process running, and reopening afterwards worked rather than throwing on a disposed window.
4. ⏳ Needs a human click — Quit exiting the process and removing the icon immediately.
5. ✅ A second launch exited 0 without starting a rival process, and surfaced the window in the running instance.

### Findings

- **`ApplicationIcon` does not make the icon loadable at runtime.** It sets the executable's Win32 icon only, so the `pack://application:,,,/app.ico` URI threw `IOException` on startup. Rather than embed a second copy as a WPF `Resource`, the tray icon is extracted from the running executable and resized to `SystemInformation.SmallIconSize`, which also guarantees the tray and Explorer show the same image.
- **Enabling both UI frameworks makes common type names ambiguous.** `Application` and `MessageBox` exist in both `System.Windows` and `System.Windows.Forms`. Global aliases in `GlobalUsings.cs` resolve them in WPF's favour once, instead of per-file.
- **Added a `DispatcherUnhandledException` handler.** A tray app has no console, so the icon crash above killed the process with output only visible because it was launched from a shell. Later phases would have lost that entirely.

---

## Phase 2: Device Manager Window ✅

**Goal:** View paired devices with live reachability, and pair new ones.

### Design decisions

- **Pairing and device management share one window.** Pairing is not frequent enough to deserve its own top-level entry, and a discovered-but-unpaired device belongs visually next to the paired ones.
- **Pairing is a state machine, not a blocking call.** `POST /pair` waits on the user tapping Accept on the phone (60s client timeout). States: *idle → discovering → pairing (cancellable) → accepted / rejected / timed out*. The UI thread must never block on any of them.
- **Discovery loops while the window is open.** `UdpDiscovery` is a one-shot 1.5s broadcast returning a snapshot. A GUI that broadcasts once shows an empty list to anyone whose phone was asleep. Re-broadcast on an interval and merge results, so devices appear as they wake.
- **Reachability polling runs only while this window is open.** A tray app pinging a phone all day is a battery complaint. No background polling in v1.
- **No local rename.** Device names sync passively from the phone — a `/clip` or `/ping` response carrying a different name overwrites the stored one. A name typed on the PC would be silently clobbered on the next send. Renaming happens on the phone; this window links to that instead.
- **Removal is confirmed and one-sided.** Removing discards the API key and requires re-pairing. The phone keeps its own stale entry until cleared there. The confirmation dialog says both things.

### Tasks

- [x] `DeviceManagerWindow` with two sections: paired devices, and devices found on the network.
- [x] Paired list rows: name, `host:port`, reachability indicator, last-seen, Remove.
- [x] Reachability polling on an interval while the window is open, via Core's `ReachabilityProbe`. Cancelled on close.
- [x] Repeating discovery loop while the window is open; merge results and exclude already-paired devices.
- [x] Pair flow: click a discovered device → confirm PC name → "Waiting for approval on <device>…" with Cancel → success, rejection, or timeout, each with a distinct message.
- [x] Manual pairing fallback: enter `ip:port` directly, for when broadcast is blocked by the network.
- [x] Remove with a confirmation dialog stating that re-pairing is required and the phone entry must be cleared separately.
- [x] Empty state when no devices are paired: explain what pairing is and point at the phone app.

### Unit Tests

- [x] Discovery result merging: the same device seen across successive broadcasts appears once; a device that stops responding is marked stale rather than vanishing mid-interaction.
- [x] Already-paired devices are excluded from the discovered list.
- [x] Pairing view-model transitions cover accepted, rejected, timed out, and user-cancelled.

### Verification

1. ⏳ Needs a phone — reachable when powered on, flipping to unreachable within one poll interval in airplane mode.
2. ⏳ Needs a phone — pairing end-to-end from the discovered list.
3. ⏳ Needs a phone — declining on the device. Covered against a fake at the view-model level.
4. ✅ Closing cancels the lifetime token, which the pairing operation is linked to; `PairStatus.Cancelled` is now distinct from `TimedOut`, and nothing is written on either. Covered by test.
5. ⏳ Needs a manual cross-check against `fling config show`. Removal through the store is covered by test.

21 view-model tests cover the state machine and list behaviour against fakes; 9 more cover `DiscoveryTracker` on the Core side. What remains is what only a real phone and a real click can exercise.

### Findings

- **`PairOperation` reported user cancellation as a timeout.** Both surface as a cancelled task, and only the token distinguishes them — so closing the window mid-pair would have told the user their phone never answered. Added `PairStatus.Cancelled`, distinguished by an exception filter on `ct.IsCancellationRequested`.
- **Added `IDeviceDiscovery`.** `UdpDiscovery` broadcasts on a real socket, so the view-model could not be tested without it. The interface is also what a future Bonjour or manual-only discovery would slot into.
- **Paired devices now self-heal their address from discovery.** Without it a phone that changed IP shows "Not reachable" with no explanation on screen, because paired devices are filtered out of the discovered list. The CLI already does this through `DeviceResolver`; the tray app would otherwise have been worse.
- **The desktop SDK replaces the implicit-using set rather than extending it.** Enabling `UseWindowsForms` on the test project dropped `System.IO` and `System.Net.Http` and added `System.Windows.Forms`. The test project sets no UI flag — the framework reference arrives transitively through the project reference.

---

## Phase 3: Fling Window ✅

**Goal:** The core interaction — stage something, look at it, send it.

### Design decisions

- **The clipboard is auto-staged when the window opens.** "I copied something, now send it" is the dominant case; requiring a paste click afterwards is a step that exists only because other sources exist. Ctrl+V and the Paste button re-read the clipboard for content copied after the window opened.
- **Sensitive clipboard content is not staged.** Password managers set the `ExcludeClipboardContentFromMonitorProcessing` and `CanIncludeInClipboardHistory` clipboard formats. When either is present, stage nothing and show an explanatory empty state. Nothing is transmitted without an explicit Fling press regardless, but rendering a vault entry on screen is its own problem.
- **Unsupported file types are rejected, not silently reinterpreted.** `FileContentResolver` falls back to sending the *file path as text* for binary files. That backstops `Send to > Fling` in Explorer, where the alternative is nothing at all. In a window with a Fling button and a preview, it reads as file transfer and delivers a useless string. The GUI rejects those types with a message naming the actual constraint: Fling sends things that can be pasted. The fallback stays CLI-only.
- **Preview is mandatory.** The CLI has `--dry-run` for this reason. Always show resolved type, size, and either a thumbnail or the text itself, before anything is sent. Sending stale clipboard content is the failure mode this exists to prevent.
- **Staged text is editable.** A `TextBox` instead of a label costs nothing and covers trimming a URL or fixing a typo.
- **No rich-text toggle.** The plan called for one, on the assumption that markup was worth preserving. It is not: the phone writes whatever arrives to its clipboard with `ClipData.newPlainText`, so HTML is pasted as visible tags. The reader now prefers plain text whenever the clipboard offers it, which is what every other application does, and a toggle would only offer a worse result. Markup reaching the phone as text in the rare fallback case is accepted, not worked around — see the Decisions Log in DESIGN.md.
- **Device selector defaults to All when more than one device is paired.** This departs from the CLI's deliberate no-default rule, which exists to prevent silently broadcasting sensitive content. The GUI's mandatory preview and explicit confirm remove that failure mode. With exactly one paired device, the selector shows that device, not "All".
- **Zero paired devices short-circuits.** Opening the Fling window with nothing paired opens the Device manager instead of a window that cannot do anything.

### Tasks

- [x] `FlingWindow` with staging area, preview, device selector, and Fling button.
- [x] Staging model with four sources: auto-staged clipboard on open, Paste button / Ctrl+V, file picker, drag-drop.
- [x] Sensitive-clipboard-format detection; empty state when detected.
- [x] Preview: thumbnail with pixel dimensions for images; editable text box with character count for text; resolved content type and payload size for both.
- [x] Reject unsupported file types with a message explaining the clipboard-tool constraint.
- [x] Warn before sending when the encoded payload exceeds `maxSizeMb`, naming the current limit.
- [x] Device selector: All (when >1 paired) plus each device. Optionally remembers the last selection, per `gui.json`.
- [x] Send with progress and Cancel. A 10MB PNG over Wi-Fi is not instant and currently has no feedback anywhere.
- [x] Per-device result display. `SendOperation` returns per-device outcomes; a partial failure must name which device failed and why.
- [x] Keyboard: Enter sends, Esc closes, Ctrl+V pastes.
- [x] Window closes on full success; stays open showing results on partial or total failure.
- [x] Open near the cursor, on the monitor the cursor is on.

### Unit Tests

- [x] Staging replaces rather than accumulates: file picker after clipboard leaves exactly one staged item.
- [x] Content classification matches `FileContentResolver` for image, text, and rejected-binary cases.
- [x] "Send as plain text" converts a staged HTML item to `text/plain`.
- [x] Oversized payload is flagged before any network call.
- [x] Send view-model surfaces mixed per-device results correctly.

### Verification

1. ⏳ Needs a phone — image staged on open, Enter, arrives.
2. ✅ Rich text stages as HTML with the plain-text toggle shown; covered by test.
3. ✅ Staging replaces rather than accumulates, and a binary file is rejected rather than sent as a path; covered by test.
4. ⏳ Needs a real password manager. The detection is covered against a fake at both the clipboard-reader and view-model levels.
5. ⏳ Needs a phone — the partial-failure path is covered by test, including the per-device wording.
6. ⏳ Needs a phone — cancellation is wired through the same linked token the window cancels on close.

47 view-model tests cover staging from all four sources, protected content, rejection, the size limit, target selection, and every send outcome. What remains is what only a real phone and a real password manager exercise.

### Findings

- **`IClipboardReader` now reports whether content is protected.** It previously returned `ClipboardContent?`, where null meant "empty or unsupported" — the window needs to tell *empty* from *protected* to explain itself rather than look broken. The content is still returned either way: the CLI sends it as before, and the window declines only to stage it unprompted.
- **`Ctrl+V` has to be handled before the text box sees it.** The preview is an editable `TextBox`, so the default paste would drop text into the preview instead of restaging from the clipboard. Handled in `OnPreviewKeyDown`.
- **Bitmap previews need `BitmapCacheOption.OnLoad`.** Without it the `BitmapImage` keeps reading from its stream lazily, and the `MemoryStream` is gone by then.
- **Four more type ambiguities from having both UI frameworks** — `DragEventArgs`, `DataFormats`, `DragDropEffects`, `OpenFileDialog`. All resolved in `GlobalUsings.cs`; `Clipboard` is deliberately left ambiguous so any direct use has to be a conscious choice.
- **`GuiSettingsStore` arrived early.** It is listed under Phase 4, but the remembered device persists, so building it here beat stubbing it twice.
- **The `text/html` path had never worked end to end.** The reader preferred CF_HTML, and the phone pastes what it receives as plain text — so rich text arrived as visible markup, in the shipped CLI as much as the tray app. Fixed by preferring CF_UNICODETEXT; markup is now a fallback for sources that offer nothing else. See the Decisions Log in DESIGN.md.
- **The window opens centred, not near the cursor.** Centred matches the device manager and is easier to predict; the cursor-relative placement the plan called for was more clever than useful.

---

## Phase 4: Settings Window ✅

**Goal:** Configure both shared Fling settings and GUI-only preferences, with the distinction visible.

### Design decisions

- **GUI preferences live in `%APPDATA%\Fling\gui.json`, separate from `config.json`.** See the Decisions Log in DESIGN.md. In short: GUI preferences change on nearly every interaction while `config.json` changes rarely, and `config.json` holds the API keys. Keeping the chatty writes out of the precious file avoids both the contention and the blast radius.
- **The window groups settings by scope.** "Fling settings" (shared with `fling.exe`) and "App preferences" (tray app only) are separate headed groups, so a user changing `maxSizeMb` understands it affects the CLI too.
- **`runAtStartup` is not stored in either file.** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` is the source of truth and is read live when the window opens. A cached copy lies as soon as someone disables the entry from Task Manager's Startup tab. Registry over a Startup-folder shortcut: user-scope, no admin, no COM, and it is what Task Manager actually controls.
- **A startup launch comes up minimized to tray**, with no window, via a command-line flag on the registered command.
- **Explorer "Send to" is exposed as a checkbox.** It calls the same Core code as `fling install` / `fling uninstall`. Most users would never discover it as a CLI command.

### Tasks

- [x] `SettingsWindow` with two headed groups.
- [x] Fling settings (write through Core's `ConfigStore`): max payload size, compression, PC name with the machine-name default shown as a hint, file logging.
- [x] App preferences (`gui.json`): notification mode, remember-last-device, default plain-text-for-HTML.
- [x] Run at startup: read/write `HKCU\...\Run`, re-read on window open, register with a start-minimized flag.
- [x] Explorer "Send to" checkbox over the shared `SendToIntegration`.
- [x] "Open log file" button, enabled only when logging is on, plus a button to open the config folder.
- [x] About: version, license, project link.
- [x] `GuiSettingsStore` with the same defensive posture as `ConfigStore`: missing file returns defaults, corrupt file falls back to defaults rather than throwing — GUI preferences are not worth blocking startup over.

### Unit Tests

- [x] `GuiSettingsStore` round-trips; missing file yields defaults; corrupt file yields defaults without throwing.
- [x] Startup registration writes and removes the expected registry value, and reports state accurately when the value is absent or points elsewhere.
- [x] Shared settings written by the GUI are read back by Core's `ConfigStore` unchanged.

### Verification

1. ✅ Covered by test, and confirmed against a real `fling config show`.
2. ✅ `Reload` runs whenever the window is activated, so a change made by the CLI meanwhile is picked up; covered by test.
3. ⏳ Needs a manual pass — the registry behaviour is covered by tests against a scratch key, including the Task Manager refusal, but nothing has written to the real Run key yet.
4. ⏳ Needs a manual pass against the actual Explorer menu.
5. ✅ Deleting or corrupting `gui.json` falls back to defaults rather than throwing; covered by two tests.

25 new tests: 11 against the registry under a scratch key, 14 on the view-model against fakes.

### Findings

- **Task Manager disables a startup entry without deleting it.** It records the refusal separately, under `Explorer\StartupApproved\Run`, so reading only the Run key would report the entry as enabled when it is not. `IsEnabled` checks both, and `Enable` clears a stale refusal — otherwise ticking the box would appear to do nothing.
- **A Run entry pointing at a different path is not "enabled".** Moving or reinstalling the app leaves an entry that no longer starts anything; reporting it as on would be a lie.
- **`--minimized` exists after all.** It was dropped when launching produced no window, but launching now always opens the Fling window, so the sign-in entry needs a way to say otherwise. It also makes the second-instance signal conditional: a sign-in launch that finds the app already running exits quietly instead of popping a window at boot.
- **Launching the app always opens the Fling window**, whether that starts a new instance or surfaces the running one. A tray icon appearing and nothing else reads as a failed launch. With no devices paired this lands on the Device manager instead, which makes a first run open on pairing.
- **The startup entry heals a moved executable.** Detecting the mismatch and reporting the checkbox as off was honest but unhelpful: the entry still launched nothing. On startup, an entry naming a file that no longer exists is repointed at the running copy. Deliberately narrow — an entry is never created, a copy that still exists is never displaced, and a refusal recorded in Task Manager survives, since correcting a path is not consent to start again.
- **The registered command carries arguments, so the path check has to parse it.** Comparing the whole value against a path would never match, and `Path.GetFullPath` throws on the quoted form rather than returning a non-match.
- **Windows open on the display holding the foreground window.** Centring on the primary display puts the window on the wrong monitor for anyone working on a second one. The foreground window is captured in the constructor, before this window becomes the foreground one, and placement runs in physical pixels through the window handle — WPF coordinates are awkward to reason about across monitors with different scaling.
- **Registry tests run against a scratch key.** `StartupRegistration` takes its key paths, so tests exercise the real registry code without ever touching what actually launches at sign-in — and clean up after themselves, parent key included.
- **Settings apply as they are changed.** No Save button to forget; the only validation is on maximum size, which rejects zero and below the way `fling config set` does.

---

## Phase 5: Notifications & Tray Polish ✅

**Goal:** Sensible feedback for sends, without becoming noise.

### Design decisions

- **Balloon tips via `NotifyIcon.ShowBalloonTip`, not the Windows toast APIs.** Proper toasts from an unpackaged .NET app require an AppUserModelID and a Start Menu shortcut before they render at all. On Windows 10/11 a balloon tip is surfaced as a real toast anyway. Revisit only if notification action buttons become necessary.
- **Failure-only is the default.** A success toast per send is noise on a tool used twenty times a day. Three modes: Always, Failures only, Never. Success at the default setting is signalled by a brief tray icon change instead.
- **Notifications fire for sends started from any window**, so the user can send and immediately move on.

### Tasks

- [x] Notification service over `ShowBalloonTip`, honouring the configured mode.
- [x] Failure notifications name the device and the reason, distinguishing auth failure from unreachable — the CLI already separates these as exit codes 3 and 2.
- [x] Brief tray icon state change on success.
- [x] Tray tooltip shows paired device count.
- [x] `--minimized` startup flag suppresses any window on launch.
- [x] Verify tray icon behaviour across DPI changes and Explorer restart (a `NotifyIcon` is lost when Explorer restarts unless re-registered).

### Verification

1. ⏳ Needs a phone for the failure case. The choice of what each outcome produces, and the wording, are covered by test.
2. ⏳ Needs a phone. The mode logic is covered by test for all three settings.
3. ✅ Verified — `--minimized` starts into the tray with no window, and a second such launch does not disturb the running one.
4. ⏳ Needs a manual pass. WinForms `NotifyIcon` listens for the `TaskbarCreated` message and re-adds itself, so no code was needed, but that is worth confirming rather than assuming.
5. ⏳ Needs a manual pass across monitors with different scaling.

14 notifier tests cover the decision table, the wording, and the balloon length limits.

### Findings

- **Balloon text has to fit by construction.** Windows truncates past 255 characters silently, and it is the tail — the device name, or what went wrong — that gets lost. Two tests written against many failing devices and against long names and errors both failed on the first run, which is what they were for. Failures now list at most three names and count the rest, and long names and error text are shortened.
- **`WindowManager` takes a factory rather than calling `new`.** Windows needed dependencies the manager had no business knowing about, and the parameterless constructors each built their own stores — a second composition root that would quietly drift from the real one. They are gone; `App` composes everything.
- **The success flash needs its own icon.** There is only one icon in the executable, so a marker is composited onto it at startup. `Icon.FromHandle` owns an unmanaged handle that `Dispose` does not release, so the handle is kept and destroyed explicitly.
- **The tooltip refreshes when a window closes.** The paired device count changes in the Device manager, and there is no change notification to subscribe to; a window closing is a good enough moment to re-read it.

---

## Phase 6: Packaging & Distribution ✅

**Goal:** A shippable tray app alongside the existing CLI.

### Design decisions

- **The tray app releases independently under a `gui/vX.Y.Z` tag.** The repo already releases per component — `cli/v1.0.0`, `android/v1.0.0`, `android/v1.0.1` — with Android running ahead of the CLI. The release axis is the shipped artifact, not the source directory, so the tray app living inside the PC-side solution does not make it part of the CLI's release.
- **Independent version numbers, not a shared one.** The CLI is stable; the tray app has six phases of churn ahead. A shared version means either republishing byte-identical CLI binaries under bumped versions or holding tray releases behind CLI ones. Compatibility is expressed the way the existing scripts already express it: a cross-reference line in the release notes naming the latest tag of the other components.
- **Its own zip, containing only the tray app.** After Phase 0 the CLI zip should be roughly 20 MB; the tray app carries WPF untrimmed and will be several times that. Bundling them would inflate the CLI download for an audience that wants a scriptable binary, and vice versa. The release notes link to the other release instead.
- **No PE subsystem patching.** The two-exe `fling.exe`/`flingw.exe` trick exists because a console app needs both a console and a no-console variant. A WPF app is GUI-subsystem natively.
- **Config compatibility across version skew is handled in code, not by release discipline.** See the `[JsonExtensionData]` decision in Phase 0. Users will run mismatched front-end versions and neither release process can prevent it.

### Tasks

- [x] Extend `publish.ps1` to produce `FlingTray.exe` (self-contained, single-file, compressed) and its own zip.
- [x] Add `gui/scripts/release.ps1`, or parameterise the existing script by tag prefix, csproj path, and CHANGELOG path. Three near-identical copies is the point at which extraction pays for itself.
- [x] Cross-reference the latest `cli/v*` and `android/v*` tags in the tray app's release notes, matching the existing convention.
- [x] Separate `CHANGELOG.md` for the tray app, versioned independently.
- [x] `packaging/README.txt` covers first-run: launch, pair, fling.
- [x] Root `README.md` and `cli/README.md` describe both front-ends, state that neither requires the other, and link both releases.
- [x] Confirm CI still covers both solutions after any project changes made in this phase.
- [x] Smoke test on a clean Windows machine with no .NET runtime installed.

### Verification

1. ⏳ Needs a machine without the .NET runtime.
2. ⏳ Needs a phone.
3. ⏳ Needs a phone for the send itself; both front-ends run side by side without complaint.
4. ✅ Concurrent writes are covered by test, and the two now genuinely run at once.
5. ✅ Covered by the `[JsonExtensionData]` tests from Phase 0.

The release script's guard rails were confirmed by running it — it refused on a dirty
working tree, and refused a version with no matching CHANGELOG section. The helper's
non-git functions were exercised directly.

### Findings

- **ReadyToRun is wrong for the tray app, for the same reason it is right for the CLI.** Untrimmed, the framework assemblies keep the precompiled code Microsoft ships them with; adding ReadyToRun recompiles them into a larger payload with nothing to gain. Measured 68.6 MB → 72.7 MB, cold start 1.9 s → 4.7 s, warm 0.70 s → 0.76 s. Worse on every axis, so it is off here and on there.
- **The tray app is 68.6 MB against the CLI's 15.9 MB**, and there is no closing that gap: WPF cannot be trimmed. This is the reason the two ship as separate downloads rather than one.
- **The release ceremony was extracted rather than copied a third time.** `scripts/ReleaseCommon.ps1` holds the guard rails, CHANGELOG extraction, compatibility cross-reference, confirmation, and publish; the CLI and tray scripts supply their version, tag, and artifact. The Android script was deliberately left alone — it is a different toolchain, and it is the one release path that cannot be rehearsed without publishing.
- **`publish.ps1` refuses to run while the tray app is running.** A running instance holds a lock on the executable, and the build failure it produces reads like a code error. It bit me repeatedly during development.

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
