# CLI Tool — Implementation Plan

This is a **live document** tracking the phased implementation of the Fling Windows CLI tool (`fling.exe`). Each phase is a vertical slice that can be built, verified, and committed independently.

The CLI is the **client** in this architecture — it sends content to the Android app's HTTP server. Phases 1–4 can be developed and unit-tested independently of the Android app. Integration testing against the phone starts at Phase 5.

---

## Phase 1: Project Scaffolding & Configuration

**Goal:** A .NET 8 console app that can read and write a config file at `%APPDATA%\Fling\config.json`.

### Tasks

- [ ] Create a .NET 8 console project (`cli/Fling.csproj`). Target `net8.0`, output type `Exe`, single-file publish ready.
- [ ] Add NuGet dependencies: `System.CommandLine` (command-line parsing), `System.Text.Json`.
- [ ] Define config model classes:
  - `FlingConfig`: `Devices: List<DeviceConfig>`, `MaxSizeMb: int = 10`, `Compress: bool = true`.
  - `DeviceConfig`: `Name: string`, `Host: string`, `Port: int = 7291`, `ApiKey: string`, `Default: bool`.
- [ ] Create `ConfigStore`:
  - `Load()` — reads `%APPDATA%\Fling\config.json`. Returns defaults if file doesn't exist.
  - `Save(FlingConfig)` — writes config. Creates the directory if needed.
- [ ] Wire up a top-level `fling` command with `System.CommandLine` (no subcommands yet — just `--version`).

### Unit Tests

- [ ] `ConfigStore` round-trips: save a config, load it back, assert equality.
- [ ] `ConfigStore` handles missing file — returns defaults.
- [ ] `ConfigStore` handles corrupt JSON — throws a clear exception (not a cryptic deserialization error).
- [ ] `DeviceConfig` defaults: port is 7291, no device is default by default.

### Verification

1. `dotnet run -- --version` → prints version string.
2. First run creates `%APPDATA%\Fling\` directory (empty, no config yet until pairing).
3. All unit tests pass: `dotnet test`.

---

## Phase 2: Device Management & `fling config`

**Goal:** Users can view and manage their config from the command line.

### Tasks

- [ ] Implement `fling config show` — pretty-prints the current config (devices, settings).
- [ ] Implement `fling config set <key> <value>` — update top-level settings (`maxSizeMb`, `compress`). Validate values.
- [ ] Implement `fling config default <device-name>` — set a device as the default target.
- [ ] Implement `fling config remove <device-name>` — remove a paired device.
- [ ] Add a `DeviceResolver` helper:
  - Given an optional `--device <name>` argument, resolve which device(s) to target.
  - If `--device` is provided, find by name (case-insensitive).
  - If `--all` is provided, return all devices.
  - Otherwise, return the default device. Error if no default is set.

### Unit Tests

- [ ] `DeviceResolver` returns the default device when no argument is given.
- [ ] `DeviceResolver` errors when no default is set and no `--device` specified.
- [ ] `DeviceResolver` finds device by name (case-insensitive).
- [ ] `DeviceResolver` with `--all` returns all devices.
- [ ] `fling config set` validates known keys and rejects unknown ones.

### Verification

1. `fling config show` — shows empty device list (no devices paired yet).
2. Manually add a device entry to the config JSON, then `fling config show` — device appears.
3. `fling config default <name>` — config file updates.
4. `fling config remove <name>` — device removed from config file.
5. All unit tests pass.

---

## Phase 3: HTTP Client & `fling pair`

**Goal:** The CLI can pair with an Android device over the network.

### Tasks

- [ ] Create `FlingHttpClient` — wraps `HttpClient` for communication with the Android server:
  - Sets `X-Fling-Key` header on authenticated requests.
  - Handles JSON serialization/deserialization.
  - Configurable timeout (default 10 seconds for normal requests, 60 seconds for pairing to allow user approval time).
- [ ] Implement `fling pair <ip:port>`:
  - Parse the `ip:port` argument (support `ip` alone, defaulting port to 7291).
  - Generate a cryptographically random API key (32 bytes, base64url-encoded).
  - Send `POST /pair` with `{ "name": "<hostname>", "key": "<generated-key>" }`.
  - On `"status": "accepted"`: save the device to config. Print success message with device name.
  - On `"status": "rejected"`: print rejection message. Don't save.
  - On timeout/connection error: print a clear error message.
- [ ] If a device with the same host:port already exists in config, prompt (or use `--force`) before re-pairing.

### Unit Tests

- [ ] API key generation: 32 bytes, base64url, no padding, unique across calls.
- [ ] IP:port parsing: `192.168.1.50:7291`, `192.168.1.50` (default port), `[::1]:7291` (IPv6), invalid inputs.
- [ ] `FlingHttpClient` serializes the pair request body correctly.
- [ ] `FlingHttpClient` handles accepted, rejected, timeout, and connection-refused responses.
  - Use a mock/fake `HttpMessageHandler` — no real network calls in unit tests.

### Verification

1. Start the Android app (Phase 3+ of the Android plan).
2. `fling pair 10.0.2.2:7291` (emulator host address, or use `adb forward` and `localhost`).
3. Accept on the phone → CLI prints success, device saved to config.
4. `fling config show` → new device appears with the generated key.
5. Attempt pairing again → idempotent (accepted immediately, config unchanged).
6. All unit tests pass.

---

## Phase 4: Clipboard Reading & Content Preparation

**Goal:** The CLI can read the Windows clipboard and prepare content for sending (base64-encode, compress).

### Tasks

- [ ] Create `ClipboardReader`:
  - Read text from the clipboard (`text/plain`).
  - Read HTML from the clipboard (`text/html`), if available.
  - Read image from the clipboard (bitmap → PNG bytes).
  - Detect which formats are available and pick the best one (image > HTML > text).
  - Handle the clipboard being empty or locked by another process.
- [ ] Create `ContentEncoder`:
  - `Encode(type, rawBytes)` → `{ type, data (base64), compressed (bool), timestamp }` as a JSON-ready object.
  - If `compress` is enabled and type is text (`text/plain`, `text/html`), GZip the raw bytes, then base64-encode the compressed output. Set `compressed: true` in the result.
  - Enforce max size check (against raw bytes, before encoding). Error if exceeded.
- [ ] Read image from a file path (`--image <path>`): load file bytes, detect PNG/convert if needed.
- [ ] Read literal text from argument (`--text "content"`): encode as UTF-8 bytes.

### Unit Tests

- [ ] `ContentEncoder` base64 round-trip: encode, decode, assert equal.
- [ ] `ContentEncoder` GZip: compress, decompress, assert equal to original.
- [ ] `ContentEncoder` skips GZip for `image/png`.
- [ ] `ContentEncoder` rejects content over max size.
- [ ] `ContentEncoder` sets `compressed: true` only for text types when compression is enabled.
- [ ] `ContentEncoder` sets `compressed: false` for `image/png` regardless of config.
- [ ] `ClipboardReader` — test the format-priority logic (image > HTML > text) using an interface/mock for the actual clipboard access.
- [ ] Image file loading: valid PNG, non-existent file, non-image file.
- [ ] Literal text: empty string, Unicode, very long string.

### Verification

1. Copy text to clipboard → `dotnet run -- send --clipboard --dry-run` → prints the content type and encoded size. (`--dry-run` is a debugging flag added in this phase; does everything except the HTTP call.)
2. Copy an image (e.g., screenshot) → `--dry-run` detects `image/png`.
3. `fling send --image test.png --dry-run` → detects file, encodes as PNG.
4. `fling send --text "hello" --dry-run` → encodes as `text/plain`.
5. All unit tests pass.

---

## Phase 5: `fling send` — Integration

**Goal:** The CLI sends clipboard content to the Android app. The core user flow is complete.

### Tasks

- [ ] Implement `fling send`:
  - Determine content source: `--clipboard` (default if none specified), `--image <path>`, or `--text "content"`.
  - Resolve target device(s) via `DeviceResolver` (`--device <name>` or `--all` or default).
  - Encode content via `ContentEncoder`.
  - Send `POST /clip` via `FlingHttpClient` to each target device.
  - Print result per device: success, auth error, connection error, etc.
- [ ] If `--all`, send to all devices concurrently (`Task.WhenAll`). Report per-device results.
- [ ] If the send fails with `401`, suggest re-pairing.
- [ ] Add `--verbose` flag for debugging (print request/response details).

### Unit Tests

- [ ] Send orchestration: mock `FlingHttpClient`, verify it's called with correct device, headers, and body.
- [ ] `--all` sends to every device and aggregates results.
- [ ] Auth failure (401) produces a re-pair suggestion in the output.
- [ ] Connection failure produces a clear message (not a raw exception).

### Verification

1. Pair with the Android app (Phase 3 of this plan).
2. Copy text → `fling send --clipboard` → notification appears on the phone, tap to paste, text matches.
3. `fling send --text "Hello from CLI"` → same result.
4. `fling send --image screenshot.png` → image notification on phone, tap, paste into app.
5. `fling send --device "Pixel 8"` → targets specific device.
6. Unplug/disconnect the phone → `fling send` → clear error message.
7. All unit tests pass.

---

## Phase 6: `fling status`

**Goal:** Users can check whether their paired devices are reachable.

### Tasks

- [ ] Implement `fling status`:
  - For each paired device, send `GET /ping` with the stored API key.
  - Print a table: device name, host:port, status (online/offline), version, latency.
  - Ping all devices concurrently.
  - Timeout per device: 3 seconds.
- [ ] Optional `--device <name>` to check a single device.

### Unit Tests

- [ ] Status aggregation: mock responses (mix of online, offline, timeout), verify table output.
- [ ] Single-device filter works.

### Verification

1. With the Android app running: `fling status` → shows the device as online with version and latency.
2. Stop the Android app: `fling status` → shows offline.
3. All unit tests pass.

---

## Phase 7: Greenshot Integration

**Goal:** Fling works as a Greenshot External Command Plugin target.

### Tasks

- [ ] Greenshot External Command Plugin calls an executable with the saved screenshot path as an argument.
  - Verify the exact argument format Greenshot passes (typically: `fling.exe <path-to-temp-png>`).
- [ ] Ensure `fling send --image <path>` (Phase 5) covers this use case.
- [ ] If Greenshot passes the path as a bare positional argument (not `--image`), add support for: `fling send <path>` as shorthand when the argument is a file path.
- [ ] Ensure the CLI exits with code 0 on success and non-zero on failure (Greenshot may report errors based on exit code).
- [ ] Test the end-to-end flow: screenshot on PC → Greenshot saves → invokes Fling → notification on phone.

### Unit Tests

- [ ] Bare positional argument detection: file path resolves to `--image` mode.
- [ ] Non-existent file path → clear error and non-zero exit code.

### Verification

1. Configure Greenshot: External Command → `fling.exe`, argument `{0}`.
2. Take a screenshot → select "Fling" destination → notification appears on phone with the image.
3. Tap notification → paste screenshot into a messaging app.

---

## Phase 8: Error Handling & UX Polish

**Goal:** The CLI handles all edge cases gracefully and provides clear user feedback.

### Tasks

- [ ] Consistent exit codes: 0 = success, 1 = user error (bad args, no default device), 2 = network error, 3 = auth error.
- [ ] Colored console output (if terminal supports it): green for success, red for errors, yellow for warnings.
- [ ] `fling send --clipboard` when clipboard is empty → clear message, not a crash.
- [ ] Large image warning: if the image exceeds `maxSizeMb`, error before attempting the upload.
- [ ] Connection timeout message: include the device name and host so the user knows which device failed.
- [ ] `--help` text for all commands is clear and includes examples.

### Unit Tests

- [ ] Exit code mapping: verify each error scenario produces the correct exit code.
- [ ] Empty clipboard handling.
- [ ] Oversized content rejection message includes the size and limit.

### Verification

1. `fling send --clipboard` with empty clipboard → message, exit code 1.
2. `fling send --image huge.bmp` (>10 MB) → rejected with size info, exit code 1.
3. `fling send` with unreachable device → timeout message with device details, exit code 2.
4. `fling --help`, `fling send --help`, `fling pair --help` → all show useful text.
5. All unit tests pass.

---

## Phase 9: Single-File Publish & Distribution

**Goal:** The CLI is published as a single self-contained `fling.exe` with no runtime dependencies.

### Tasks

- [ ] Configure the project for single-file publish:
  ```xml
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  ```
- [ ] Enable trimming (`PublishTrimmed`) — verify nothing breaks (System.Text.Json and System.CommandLine need trim-compatible usage).
- [ ] Add `AssemblyVersion` and wire it to `--version` output.
- [ ] Document the publish command: `dotnet publish -c Release`.
- [ ] Test the published binary on a clean Windows machine (or VM) without .NET installed.

### Unit Tests

- [ ] (No new unit tests — this is a packaging phase. Run the full existing test suite against the published binary as a smoke test.)

### Verification

1. `dotnet publish -c Release` → produces a single `fling.exe`.
2. Copy `fling.exe` to a machine without .NET → `fling --version` works.
3. Full end-to-end: `fling pair`, `fling send --text "test"`, `fling status` — all work from the published binary.

---

## Appendix: Testing Strategy

### Unit Test Setup

- Test framework: xUnit.
- Mocking: use `System.Net.Http`'s `DelegatingHandler` for `HttpClient` fakes (no external mocking library needed).
- File system tests: use a temp directory, clean up in `Dispose`.
- Clipboard tests: abstract behind `IClipboardReader` so unit tests don't touch the real clipboard.

### What to Mock vs. Integration-Test

| Layer | Unit Test (mocked) | Integration Test (real) |
|-------|-------------------|------------------------|
| Config I/O | Temp files | — |
| HTTP calls | Fake `HttpMessageHandler` | Against running Android app |
| Clipboard | `IClipboardReader` stub | Manual (copy, then run CLI) |
| Content encoding | Real (pure functions) | — |
| Device resolution | Real (pure logic) | — |

### Running Tests

```bash
cd cli
dotnet test
```
