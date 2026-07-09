# Android App — Implementation Plan

This is a **live document** tracking the phased implementation of the Fling Android app. Each phase is a vertical slice that can be built, verified, and committed independently. Phases are ordered so that each one builds on the last and is testable in isolation.

**Target AVD:** Pixel_8, API 36

**Test framework:** JUnit 4 + kotlinx-coroutines-test. Ktor routes tested via Ktor's `testApplication` (in-process, no real server). Android-specific code (notifications, clipboard) tested manually on the AVD.

---

## Phase 1: Project Scaffolding & Foreground Service ✓

**Goal:** An Android app that starts a foreground service with a persistent notification showing "Fling is running". No HTTP server yet — just the service lifecycle.

### Tasks

- [x] Create a new Android project (package: `dev.davidfdev.fling`, min SDK 26, target SDK 36).
- [x] Add Gradle dependencies: Jetpack Compose (BOM), Material 3, Ktor (server-netty, content-negotiation, kotlinx-serialization), DataStore Preferences. Note: `kotlin-android` plugin is not needed under AGP 9 (bundled via `kotlin-compose`). Accompanist not needed (Compose BOM handles permissions).
- [x] Set up the test infrastructure: JUnit 4, kotlinx-coroutines-test, Ktor test dependencies in `build.gradle.kts`.
- [x] Create `FlingService` — a foreground service that:
  - Creates a notification channel (`fling_service`) on start.
  - Posts a persistent notification: "Fling is running". Tapping the notification opens `MainActivity` (brings to front if already open).
  - Runs as `START_STICKY`.
  - Foreground service type: `specialUse`.
- [x] Create a minimal Compose `MainActivity` with a single button: Start / Stop service.
- [x] Add `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_SPECIAL_USE`, and `POST_NOTIFICATIONS` permissions to the manifest.
- [x] Handle the POST_NOTIFICATIONS runtime permission request on API 33+.

### Verification

1. ~~Build and install on Pixel_8 AVD.~~
2. ~~Tap "Start" — persistent notification appears in the shade.~~
3. ~~Tap "Stop" — notification disappears.~~
4. ~~Kill the app from recents — service continues running (START_STICKY).~~
5. ~~No crashes in Logcat.~~

---

## Phase 2: Ktor HTTP Server & `/ping` ✓

**Goal:** The foreground service embeds a Ktor HTTP server. The `/ping` endpoint responds to requests.

### Tasks

- [x] Embed a Ktor Netty server inside `FlingService`, started/stopped with the service lifecycle.
- [x] Listen on `0.0.0.0:7291`.
- [x] Install `ContentNegotiation` with `kotlinx.serialization.json`.
- [x] Implement `GET /ping` — returns `{"status":"ok","name":"<device_model>","version":"1.0.0"}`. No auth check yet. Uses `Build.MODEL` for the name (user-configurable name deferred to Phase 9).
- [x] Handle server start failure gracefully (e.g., port already in use) — log and stop the service.
- [x] Add `INTERNET` permission to the manifest.

> **Decision:** The persistent notification text stays as "Fling is running" — no IP or port shown. The device IP will be displayed in the Compose UI (Phase 8).

### Unit Tests

- [x] `GET /ping` returns 200 with correct JSON shape (`status`, `name`, `version`) — via Ktor `testApplication`.
- [x] Unknown routes return 404 (not a stack trace).

### Verification

1. ~~Start the service on the AVD.~~
2. ~~From the host PC terminal: `adb forward tcp:7291 tcp:7291` then `curl http://localhost:7291/ping`~~
3. ~~Confirm JSON response with status, name, and version.~~
4. ~~Stop the service — confirm the server stops accepting connections.~~

> **Note:** The AVD and host communicate via the emulator's network. Use `adb forward tcp:7291 tcp:7291` if the AVD IP is not directly reachable, then curl `http://localhost:7291/ping`.

---

## Phase 3: Pairing (`/pair`) ✓

**Goal:** The phone can accept pairing requests from PCs. Paired device keys are persisted in a local JSON file.

> **Decisions:**
>
> - **Storage:** Paired devices are stored in a plain JSON file (`paired_devices.json` in app internal storage) managed via kotlinx-serialization. Not DataStore Preferences — a structured list doesn't belong in scalar key-value storage.
> - **Phone name:** Uses `Build.MODEL` for now (same as `/ping`). Later (Phase 9) this becomes a persistent random name (e.g., "Handsome Orange") to avoid user-editable names causing desync between PC and phone.
> - **PC name:** The `"name"` field in the pair request body is what the PC declares itself as. The phone stores it in `PairedDevice` and displays it in the "Paired devices" list. This name is exchanged only at pair-time.
> - **Name freshness (future):** `/ping` already returns the phone's current name; the CLI can compare and update its stored copy passively. PC→phone staleness is accepted until a future sync mechanism.
> - **Concurrent pairing:** Only one pending request at a time. A second request arriving while one is pending is auto-rejected.
> - **Broadcast intents:** Pairing notification action PendingIntents must use `setPackage()` to target the app explicitly — implicit intents are blocked by `RECEIVER_NOT_EXPORTED`.

### Tasks

- [x] Define a `PairedDevice` data class: `name: String`, `apiKey: String`, `pairedAt: Long`.
- [x] Create `DeviceRepository` — reads/writes `paired_devices.json` in app internal storage via kotlinx-serialization. Exposes suspend functions for store/retrieve/delete. Reads and writes on `Dispatchers.IO`.
- [x] Implement `POST /pair`:
  - Parse request body: `{ "name": "My PC", "key": "<api-key>" }`.
  - If the key is already paired, respond `{"status":"accepted","name":"<phone_name>"}` immediately (idempotent re-pair).
  - Otherwise, suspend the request and prompt the user for approval.
  - On accept: store the device, respond `{"status":"accepted","name":"<phone_name>"}`.
  - On reject: respond `{"status":"rejected"}`.
  - If another pairing request is already pending, respond `{"status":"rejected"}` immediately.
- [x] Implement the pairing approval mechanism behind a `PairingApprover` interface (`suspend fun requestApproval(deviceName: String): Boolean`):
  - MVP implementation: show an Android notification with Accept/Reject action buttons (PendingIntent → BroadcastReceiver). The Ktor request suspends on a `CompletableDeferred<Boolean>` and resumes when the user taps an action.
  - The route handler calls only the `PairingApprover` interface — it does not know whether approval happens via notification, dialog, or anything else. This makes upgrading the UX later (e.g., full-screen dialog) a single-class swap.
- [x] Add a timeout: if the user doesn't respond within 30 seconds, auto-reject and respond `{"status":"rejected"}`.

### Unit Tests

- [x] `POST /pair` with valid body returns 200 and correct JSON shape — via `testApplication` (auto-accept in tests by providing a fake `PairingApprover`).
- [x] `POST /pair` with missing `name` or `key` returns 400.
- [x] `POST /pair` with malformed JSON returns 400.
- [x] `DeviceRepository` round-trip: store a device, retrieve it, assert fields match (use a temp file).
- [x] `DeviceRepository` delete: store, delete, confirm absent.
- [x] Idempotent re-pair: pair with same key twice, confirm only one device entry stored.
- [x] Concurrent pairing: second request while one is pending returns rejected immediately.

### Verification

1. ~~Start the service.~~
2. ~~From the PC: `curl -X POST http://localhost:7291/pair -H "Content-Type: application/json" -d '{"name":"Test PC","key":"abc123"}'`~~
3. ~~Confirm a notification appears on the phone asking to approve.~~
4. ~~Tap Accept — confirm the response is `{"status":"accepted",...}`.~~
5. ~~Repeat the same curl — confirm idempotent re-pair (accepted immediately, no prompt).~~
6. ~~Send a new key: `curl ... -d '{"name":"Other PC","key":"xyz789"}'` — tap Reject — confirm `{"status":"rejected"}`.~~
7. ~~Kill and restart the app — confirm previously paired device key is still persisted (re-pair with `abc123` should be idempotent).~~

---

## Phase 4: API Key Authentication ✓

**Goal:** The `/ping` and future `/clip` endpoints require a valid `X-Fling-Key` header. Unauthenticated requests get `401`.

### Tasks

- [x] Create a Ktor route-scoped plugin that:
  - Reads the `X-Fling-Key` header.
  - Looks it up in the `DeviceRepository`.
  - If missing or not found, responds `401 Unauthorized` with `{"error":"unauthorized"}`.
- [x] Apply via `authenticated(deviceRepository) { }` route grouping to `/ping` (and future `/clip`). `/pair` remains outside (no key required).
- [x] Return a structured 401 JSON body, not a Ktor default HTML error page.

### Unit Tests

- [x] Request without `X-Fling-Key` header → 401 with JSON body — via `testApplication`.
- [x] Request with unknown key → 401.
- [x] Request with valid key → 200 (passes through to the route handler).
- [x] `/pair` is not affected by the interceptor (no key required).

### Verification

1. ~~Pair a test device (Phase 3).~~
2. ~~`curl http://localhost:7291/ping` (no header) → 401.~~
3. ~~`curl http://localhost:7291/ping -H "X-Fling-Key: wrong"` → 401.~~
4. ~~`curl http://localhost:7291/ping -H "X-Fling-Key: abc123"` → 200 with ping response.~~

---

## Phase 5: Receive Clipboard Content (`/clip`) ✓

**Goal:** The phone can receive text and image content via `POST /clip` and store it in an in-memory buffer.

> **Decisions:**
>
> - **Buffer ownership:** `ClipboardBuffer` is created in `FlingService` and passed into `configureFling(...)`. Later phases (notifications, UI) access it from the service.
> - **Max payload size:** Enforced on the decoded content (after base64 decode + gunzip), not on the raw HTTP body. Checking after decode is sufficient for MVP.

### Tasks

- [x] Define a `ClipItem` data class: `type: String` (MIME), `data: ByteArray` (decoded from base64), `timestamp: Long`, `receivedAt: Long`.
- [x] Create `ClipboardBuffer` — an in-memory ring buffer holding the last 10 items. Thread-safe.
- [x] Implement `POST /clip`:
  - Requires `X-Fling-Key` authentication (Phase 4 interceptor).
  - Parse request body: `{ "type": "text/plain", "data": "<base64>", "timestamp": 1720100000 }`.
  - Validate `type` is one of: `text/plain`, `text/html`, `image/png`.
  - Decode `data` from base64.
  - If `"compressed": true` in the JSON body, gunzip the decoded bytes (for text types). This is application-level compression — the HTTP `Content-Encoding` header is not used.
  - Enforce max payload size (default 10 MB on the decoded content). Return `413` if exceeded.
  - Add to `ClipboardBuffer`.
  - Respond `{"status":"ok"}`.
- [x] Return appropriate error responses:
  - `400` for malformed JSON or unsupported type.
  - `413` for oversized payloads.

### Unit Tests

- [x] `POST /clip` with valid text payload → 200, item added to buffer — via `testApplication`.
- [x] `POST /clip` with valid `image/png` payload → 200.
- [x] Unsupported MIME type → 400.
- [x] Malformed JSON → 400.
- [x] Payload exceeding max size → 413.
- [x] Missing required fields (`type`, `data`) → 400.
- [x] GZip-encoded text: send with `"compressed": true`, verify gunzipped content matches original.
- [x] `ClipboardBuffer`: add items, verify FIFO eviction at capacity (ring buffer behavior).
- [x] `ClipboardBuffer`: thread safety — concurrent writes don't corrupt state (use `runBlocking` + coroutines).

### Verification

1. ~~Pair a device.~~
2. ~~Send plain text → `{"status":"ok"}`.~~
3. ~~Send with a bad key → 401.~~
4. ~~Send with an unsupported type (e.g., `application/pdf`) → 400.~~
5. ~~Send malformed JSON → 400.~~
6. ~~Send gzip-compressed text with `"compressed": true` → `{"status":"ok"}`.~~

---

## Phase 6: Notifications & Tap-to-Copy ✓

**Goal:** When content arrives, a notification appears. Tapping the notification copies the content to the phone's clipboard.

> **Decisions:**
>
> - **Notification posting:** A `ContentNotifier` interface (similar to `PairingApprover`) is passed into `configureFling()`. The `/clip` route calls `contentNotifier.notify(item)` after adding to the buffer. The service provides the real implementation (`NotificationContentNotifier`) with `Context` access. Tests use a fake.
> - **Image clipboard:** Images are written to a temp file and exposed via `FileProvider` (`content://` URI). Requires a `FileProvider` declaration in the manifest and a `file_paths.xml` resource.
> - **Notification tap mechanism:** A `BroadcastReceiver` handles the tap action (consistent with the pairing pattern). The receiver copies content to `ClipboardManager`, shows a toast, and dismisses the notification.
> - **Notification IDs:** `AtomicInteger` counter gives each clip a unique notification ID. The receiver dismisses the specific notification on tap.

### Tasks

- [x] Create a second notification channel (`fling_content`) for received content notifications.
- [x] When a `ClipItem` is added to the buffer, post a notification:
  - **Text:** Show a preview (first ~100 characters, truncated).
  - **Image:** Show the image as a `BigPictureStyle` notification thumbnail.
  - Each notification gets a unique ID (use a counter or timestamp).
- [x] On notification tap (PendingIntent → BroadcastReceiver or Activity):
  - Write the content to the system clipboard (`ClipboardManager`).
  - For `text/plain` and `text/html`: set as `ClipData.newPlainText` / `newHtmlText`.
  - For `image/png`: write to a temporary `FileProvider` URI and set as `ClipData.newUri`.
  - Show a toast: "Copied to clipboard".
  - Dismiss the notification.
- [x] Auto-expire notifications after 5 minutes (use `setTimeoutAfter` on the notification builder).

### Verification

1. ~~Send text via curl — notification appears with text preview.~~
2. ~~Tap notification — content copied to clipboard, toast shown.~~
3. ~~Long text truncated in preview, full text copied on tap.~~
4. ~~Send image (10x10 PNG) — notification with BigPictureStyle preview appears.~~
5. ~~Tap image notification — image URI copied to clipboard.~~

---

## Phase 7: Rate Limiting ✓

**Goal:** The Android server enforces rate limiting to protect itself from runaway clients.

> **Decisions:**
>
> - **Algorithm:** Sliding window — track timestamps of recent requests per key, reject when count in the last 60s exceeds the limit. Simple and predictable for MVP.
> - **Enforcement:** A separate route-scoped plugin applied alongside auth inside the `authenticated` block. Auth checks identity, rate limiter checks volume — concerns stay separate.
> - **Clock source:** Injectable `() -> Long` time provider (defaulting to `System.currentTimeMillis()`) so unit tests can control time without real delays.
> - **Memory cleanup:** Skipped for MVP — paired device count is tiny, per-key timestamp lists are negligible.

### Tasks

- [x] Implement a sliding-window rate limiter: 10 requests per minute, per API key.
- [x] Apply as a route-scoped plugin to `/clip` and `/ping`. Exclude `/pair`.
- [x] When rate limited, respond `429 Too Many Requests` with `{"error":"rate_limited"}`.

### Unit Tests

- [x] Rate limiter allows up to the configured limit within the window.
- [x] Rate limiter rejects requests beyond the limit.
- [x] Rate limiter resets after the window expires (use a controllable clock / `TestCoroutineScheduler`).
- [x] Per-key isolation: key A at limit does not block key B.
- [x] `/pair` is not rate-limited.

### Verification

1. ~~Send 10 rapid requests to `/ping` → first 10 succeed (200).~~
2. ~~11th request → 429.~~
3. ~~`/pair` still returns 200 while rate limited — not affected.~~

---

## Phase 8: Compose UI — Main Screen ✓

**Goal:** A Compose UI showing service status, paired devices, and recent clipboard items.

> **Decisions:**
>
> - **Data flow:** `DeviceRepository` and `ClipboardBuffer` are owned by a custom `Application` subclass (`FlingApplication`). Both the service and UI access them from there.
> - **Reactivity:** Both `ClipboardBuffer` and `DeviceRepository` expose `StateFlow` directly. `ClipboardBuffer` emits `List<ClipItem>` on every `add()`. `DeviceRepository` emits `List<PairedDevice>` on `store()` and `delete()`. ViewModel collects these flows — no polling.
> - **IP address:** Uses `NetworkInterface` enumeration (no extra permissions needed). Displays the first non-loopback IPv4 address, or "Not connected" if none found.
> - **Service state:** A `StateFlow<Boolean>` in `FlingApplication` that the service sets on start/stop. The UI observes it.
> - **Unpair confirmation:** Tapping a paired device shows a confirmation dialog before removing it.

### Tasks

- [x] Design a single-screen layout:
  - **Top section:** Service status card with toggle (listening / stopped), device IP and port display.
  - **Middle section:** List of paired devices (name, paired date). Tap to unpair with confirmation dialog.
  - **Bottom section:** Recent items list (text preview or image label, timestamp). Tap to copy.
- [x] Wire up to `DeviceRepository` (paired devices) and `ClipboardBuffer` (recent items) using StateFlows.
- [x] Observe service state — update the toggle when the service starts/stops.
- [x] Material 3 theming with system dynamic colors where available, light/dark mode support.

### Verification

1. ~~Open the app — UI renders with service toggle.~~
2. ~~Start service — status updates, IP displayed.~~
3. ~~Pair a device via curl — device appears in the list.~~
4. ~~Send content via curl — item appears in the recent list.~~
5. ~~Tap a recent item — copied to clipboard (toast confirms).~~
6. ~~Tap a paired device — confirmation dialog appears, remove works.~~

---

## Phase 9: Settings & Configuration ✓

**Goal:** User can configure port and device name. Service auto-starts on app launch if previously enabled.

> **Decisions:**
>
> - **Scope:** Trimmed to essentials for MVP — port, device name, service auto-start. Rate limit, max payload, notification timeout, and buffer size remain hardcoded.
> - **Storage:** DataStore Preferences (three scalar values: port int, device name string, service enabled boolean).
> - **Device name:** Random two-word name generated on first launch from an embedded word list (~50 adjectives + ~50 nouns). Free-text editable in Settings with a "regenerate" button. Validated non-blank.
> - **Settings navigation:** Separate Compose screen accessible via a gear icon in the main screen's top app bar.
> - **Port change UX:** Shows a message "Restart the service for changes to take effect." Auto-restart deferred.
> - **Auto-start:** In `FlingApplication.onCreate()` — starts the foreground service if `serviceEnabled` was true when the app was last used. Works for user-initiated launches; boot-start deferred to Phase 10.

### Tasks

- [x] Create a `Settings` data class with defaults: `port: Int = 7291`, `deviceName: String = <random>`, `serviceEnabled: Boolean = false`.
- [x] Store settings in DataStore Preferences.
- [x] **Persistent device name:** Generate a random two-word name on first launch from an embedded word list. Used as the phone's identity in `/ping` and `/pair` responses.
- [x] Add a Settings screen (Compose) accessible via gear icon in the top app bar. Fields: port (numeric), device name (free-text + regenerate button).
- [x] Changing the port shows a message: "Restart the service for changes to take effect."
- [x] Wire settings into the Ktor server (port, device name).
- [x] **Remember last state + auto-start:** Persist the service toggle state in DataStore. On app launch, auto-start the service in `FlingApplication.onCreate()` if it was previously enabled.
- [x] Display device name on main screen status card when service is running.

### Verification

1. ~~Open Settings — device name is a random two-word name, port is 7291.~~
2. ~~Gear icon in top bar opens Settings screen.~~
3. ~~`/ping` and `/pair` responses use the random device name.~~
4. ~~Service auto-starts on app reopen when previously enabled.~~
5. ~~Device name displayed on main screen status card.~~

---

## Phase 10: Polish & Edge Cases ✓

**Goal:** Handle real-world edge cases and polish the experience.

> **Decisions:**
>
> - **Wi-Fi awareness:** Use `ConnectivityManager` with `NetworkCallback` for live updates. Display a warning banner in the `ServiceStatusCard` on the Main Screen (not as a notification). Requires `ACCESS_NETWORK_STATE` permission.
> - **Battery optimization:** A button in the Settings screen that opens `Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS`. Wrapped in `runCatching` since some OEMs crash on this intent. Check status via `PowerManager.isIgnoringBatteryOptimizations()`, re-check on `onResume`. If already exempted, show "Battery optimization disabled" (non-actionable).
> - **Boot auto-start:** `BOOT_COMPLETED` BroadcastReceiver that checks `settingsRepository.serviceEnabled` and starts the foreground service if true. Requires `RECEIVE_BOOT_COMPLETED` permission.
> - **ProGuard / R8:** Enable optimization in release build type. Add keep rules for Ktor Netty (`io.netty.**`, `io.ktor.**`) and kotlinx-serialization (`@Serializable` classes). Verify release APK works on AVD.
> - **App icon:** Classic paper airplane (white on blue #2563EB). Minimal, clean design — pointing upper-right. Matches the existing notification icon theme. Adaptive icon with round/square mask support.
> - **Concurrent pairing / error handling:** Already handled in earlier phases (Phase 3 rejects concurrent; Phases 2-5 return clean JSON errors). No additional work needed.

### Tasks

- [x] **Boot auto-start:** Add `RECEIVE_BOOT_COMPLETED` permission and a `BootReceiver` that reads `settingsRepository.serviceEnabled` and starts `FlingService` if true.
- [x] **Wi-Fi awareness:** Add `ACCESS_NETWORK_STATE` permission. Create a `ConnectivityObserver` using `NetworkCallback` exposing a `StateFlow<Boolean>` for Wi-Fi connected state. Display a warning in `ServiceStatusCard` when not on Wi-Fi and service is running.
- [x] **Battery optimization:** Add a "Battery optimization" row in SettingsScreen. Check `PowerManager.isIgnoringBatteryOptimizations()`. If not exempted, show a button that launches `Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS` wrapped in `runCatching`. Re-check on resume via `LifecycleEventObserver`.
- [x] **ProGuard / R8:** Set `optimization { enable = true }` in the release build type. Add `proguard-rules.pro` with keep rules for Ktor, Netty, and kotlinx-serialization `@Serializable` classes.
- [x] **App icon:** Replace adaptive icon resources (`ic_launcher` foreground/background). White paper airplane on #2563EB blue background. Uses vector drawables via `mipmap-anydpi` (minSdk 26).

### Verification

1. Turn off Wi-Fi on the AVD — warning appears in the status card.
2. Turn Wi-Fi back on — warning disappears (live update via NetworkCallback).
3. Reboot the AVD with service previously enabled — service restarts automatically.
4. Open Settings — battery optimization row shows current status.
5. Build a release APK with R8 enabled — install and verify all features work.
6. App icon appears correctly in the launcher.

---

## Post-Phase Refinements ✓

Changes made after all 10 phases were complete.

### Recent Clips — actions dialog & clear

- Tapping a clip row opens a dialog with **Copy**, **Share**, and **Clear** options (previously tap-to-copy for text, no-op for images).
- Copy works for both text and images (images via `FileProvider` URI).
- Share uses `Intent.ACTION_SEND` with appropriate MIME type.
- Clear removes the individual clip from the buffer.
- A **Clear** button in the "Recent Clips" section header clears all clips (hidden when list is empty).
- `ClipboardBuffer` gained `remove(item)` and `clear()` methods.

### Content notification actions

- Notification actions: **Copy** and **Share** buttons (do not dismiss the notification).
- Tapping the notification body still copies to clipboard and dismisses (existing behavior).
- Three broadcast actions: `TAP_COPY_CLIP` (tap, copies + dismisses), `COPY_CLIP` (button, copies only), `SHARE_CLIP` (button, opens chooser).

### Service notification — lock screen visibility

- Added `VISIBILITY_SECRET` to the persistent "Fling is running" notification so it does not appear on the lock screen.
- Also added `setSilent(true)`, `setShowWhen(false)`, `setOnlyAlertOnce(true)`.

### Release signing

- `build.gradle.kts` loads `keystore.properties` (git-ignored) for release signing. Falls back to debug signing when absent.
- `keystore.properties.template` checked in with the `keytool` command.
- `base.archivesName` set to `"fling"` — APKs are `fling-debug.apk` / `fling-release.apk`.

---

## Phase 11: UDP Discovery Listener ✓

**Goal:** The Android app responds to UDP broadcast discovery requests from the CLI, enabling auto-discovery on the local network.

**Context:** The CLI stores a fixed IP per paired device. When the phone's IP changes (e.g., switching networks), the user must re-pair manually. This phase adds a UDP listener so the CLI can find the phone automatically. See `cli_progress.md` Phase 11 for the CLI side.

> **Decisions:**
>
> - **Mechanism: UDP broadcast.** Zero dependencies. The CLI broadcasts `FLING?` to `255.255.255.255:7290`; the phone responds with `FLING:<port>:<device_name>` via unicast.
> - **Discovery port:** UDP 7290 (one below the HTTP port 7291).
> - **MulticastLock:** Required to receive UDP broadcasts on Android. Acquired only while the listener is active. Uses `WifiManager.createMulticastLock()`. Requires `CHANGE_WIFI_MULTICAST_STATE` permission (normal, no runtime prompt).
> - **WiFi-only gate:** Only listen when connected to Wi-Fi. Use the existing `ConnectivityObserver` from Phase 10. Acquire the `MulticastLock` and open the UDP socket when Wi-Fi connects; release and close when Wi-Fi disconnects. This avoids holding the lock on mobile data where broadcast discovery can't work anyway.
> - **Lifecycle:** The UDP listener starts and stops with `FlingService`. It is a lightweight addition to the existing foreground service — not a separate service.
> - **Device name:** Read fresh from `SettingsRepository` on each discovery request (not cached at listener start), so name changes take effect without a service restart.

### Tasks

- [x] Add `CHANGE_WIFI_MULTICAST_STATE` permission to the manifest.
- [x] Create `DiscoveryListener`:
  - Opens a `DatagramSocket` on UDP port 7290.
  - Listens for incoming `FLING?` packets.
  - Responds to the sender's address with `FLING:<port>:<device_name>` (port from settings, device name from settings).
  - Runs on a background coroutine tied to the service lifecycle.
- [x] Integrate `MulticastLock` management:
  - Acquire `WifiManager.MulticastLock` when the listener starts.
  - Release when the listener stops.
- [x] Wire WiFi-only gate using the existing `ConnectivityObserver`:
  - Start the `DiscoveryListener` (and acquire lock) when Wi-Fi is connected.
  - Stop the listener (and release lock) when Wi-Fi disconnects.
  - On service start, check current Wi-Fi state to decide initial listener state.
- [x] Start/stop `DiscoveryListener` with `FlingService` lifecycle.

### Unit Tests

- [x] `DiscoveryListener` responds with correct `FLING:<port>:<name>` format.
- [x] `DiscoveryListener` ignores non-`FLING?` packets.
- [x] Response includes the configured port and device name (not hardcoded values).

### Verification

1. Start the Fling app on a phone connected to Wi-Fi.
2. From the PC on the same network, send a UDP broadcast to port 7290 — receive a response with the phone's port and device name.
3. Disconnect Wi-Fi on the phone — listener stops (no response to broadcasts).
4. Reconnect Wi-Fi — listener resumes.
5. Stop the Fling service — listener stops.

---

## Appendix: Device Install Script

`android/scripts/install-device.ps1` builds and installs the APK on a USB-connected physical device. Adapted from StelaApp's equivalent script.

```powershell
# Default: build release and install
powershell -File scripts/install-device.ps1

# Debug build, reinstall (wipes data), set up port forwarding
powershell -File scripts/install-device.ps1 -DebugBuild -Reinstall -Forward
```

Flags: `-DebugBuild`, `-NoBuild`, `-Reinstall`, `-NoLaunch`, `-Forward` (adb forward tcp:7291), `-Force` (skip confirmation).

APK naming: `base.archivesName` is set to `"fling"`, producing `fling-debug.apk` and `fling-release.apk`.

---

## Appendix: Unit Testing Strategy

### What to Unit Test vs. AVD-Test

| Layer | Unit Test (JVM) | AVD / Manual Test |
|-------|----------------|-------------------|
| Ktor routes & responses | `testApplication` (in-process, no real server) | — |
| DeviceRepository | Test DataStore instance | — |
| ClipboardBuffer | Real (pure logic) | — |
| Rate limiter | Real with controllable clock | — |
| Settings validation | Real (pure logic) | — |
| GZip decode + base64 | Real (pure functions) | — |
| Foreground service lifecycle | — | AVD |
| Notifications (post, tap, expire) | — | AVD |
| Clipboard write (ClipboardManager) | — | AVD |
| Compose UI | — | AVD |

### Running Tests

```bash
cd android
./gradlew test          # JVM unit tests
./gradlew connectedCheck  # Instrumented tests (if any, on AVD)
```

---

## Appendix: Testing Cheat Sheet

Common curl commands for testing against the AVD (assuming `adb forward tcp:7291 tcp:7291`):

```bash
# Ping
curl http://localhost:7291/ping -H "X-Fling-Key: abc123"

# Pair
curl -X POST http://localhost:7291/pair \
  -H "Content-Type: application/json" \
  -d '{"name":"Test PC","key":"abc123"}'

# Send text (base64 of "Hello World")
curl -X POST http://localhost:7291/clip \
  -H "X-Fling-Key: abc123" \
  -H "Content-Type: application/json" \
  -d '{"type":"text/plain","data":"SGVsbG8gV29ybGQ=","timestamp":1720100000}'

# Send image (base64-encode a PNG first)
# base64 -w0 image.png > /tmp/img.b64
# Then build the JSON payload with the encoded string.
```
