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

## Phase 7: Rate Limiting

**Goal:** The Android server enforces rate limiting to protect itself from runaway clients.

### Tasks

- [ ] Implement a simple token-bucket or sliding-window rate limiter: 10 requests per minute, per API key.
- [ ] Apply to `/clip` and `/ping`. Exclude `/pair`.
- [ ] When rate limited, respond `429 Too Many Requests` with `{"error":"rate_limited"}`.

### Unit Tests

- [ ] Rate limiter allows up to the configured limit within the window.
- [ ] Rate limiter rejects requests beyond the limit.
- [ ] Rate limiter resets after the window expires (use a controllable clock / `TestCoroutineScheduler`).
- [ ] Per-key isolation: key A at limit does not block key B.
- [ ] `/pair` is not rate-limited.

### Verification

1. Send 10 rapid curl requests to `/clip` → all succeed.
2. Send an 11th → 429.
3. Wait 60 seconds → next request succeeds.
4. Confirm a different API key has its own independent limit.

---

## Phase 8: Compose UI — Main Screen

**Goal:** A Compose UI showing service status, paired devices, and recent clipboard items.

### Tasks

- [ ] Design a single-screen layout:
  - **Top section:** Service status toggle (listening / stopped), device IP and port display (this is where the IP is shown — not in the notification, per Phase 2 decision).
  - **Middle section:** List of paired devices (name, paired date). Swipe-to-delete to unpair.
  - **Bottom section:** Recent items list (text preview or image thumbnail, timestamp). Tap to copy.
- [ ] Wire up to `DeviceRepository` (paired devices) and `ClipboardBuffer` (recent items) using Flows.
- [ ] Observe service state — update the toggle when the service starts/stops.
- [ ] Material 3 theming with system dynamic colors where available, light/dark mode support.

### Verification

1. Open the app — UI renders with service toggle.
2. Start service — status updates, IP displayed.
3. Pair a device via curl — device appears in the list.
4. Send content via curl — item appears in the recent list.
5. Tap a recent item — copied to clipboard (toast confirms).
6. Swipe a paired device — confirm it's removed (and its key is deleted from DataStore).
7. Toggle dark mode in system settings — theme updates.

---

## Phase 9: Settings & Configuration

**Goal:** User can configure port, rate limit, notification timeout, and max payload size.

### Tasks

- [ ] Create a `Settings` data class with defaults: `port: Int = 7291`, `maxSizeMb: Int = 10`, `rateLimitPerMinute: Int = 10`, `notificationTimeoutMinutes: Int = 5`, `bufferSize: Int = 10`, `serviceEnabled: Boolean = true`.
- [ ] **Persistent device name:** Generate a random two-word name on first launch (e.g., "Handsome Orange", similar to LocalSend). Store in Settings. Used as the phone's identity in `/ping` and `/pair` responses. The user may override it in Settings, understanding this can cause desync with the PC's stored copy (the CLI can refresh its copy from `/ping` passively).
- [ ] Store settings in DataStore.
- [ ] Add a Settings screen (Compose) accessible from the main screen.
- [ ] Changing the port requires a service restart — prompt the user.
- [ ] Wire settings into the Ktor server, rate limiter, notification builder, and buffer.
- [ ] **Remember last state + auto-start:** Persist the service toggle state in DataStore. On app launch (and on boot in Phase 10), auto-start the service if it was previously enabled.

### Unit Tests

- [ ] `Settings` defaults: all fields have expected default values.
- [ ] Settings round-trip via DataStore: save, reload, assert equality.
- [ ] Validation: port out of range (0, 65536+), negative maxSize, negative rate limit — rejected with clear error.

### Verification

1. Open Settings — all values show defaults.
2. Change port to 7292 — service restarts — curl on new port succeeds, old port fails.
3. Change rate limit to 2 — confirm 429 after 2 requests.
4. Change notification timeout — confirm notifications expire at the new interval.

---

## Phase 10: Polish & Edge Cases

**Goal:** Handle real-world edge cases and polish the experience.

### Tasks

- [ ] **Wi-Fi awareness:** Detect when the device is not on Wi-Fi and show a warning in the UI and notification.
- [ ] **Battery optimization:** Guide the user to disable battery optimization for Fling (or the service may be killed). Show a prompt if not exempted.
- [ ] **Service auto-start on boot:** Add a `BOOT_COMPLETED` BroadcastReceiver to restart the service if it was running before reboot.
- [ ] **Concurrent pairing requests:** Queue or reject a second pairing request while one is pending.
- [ ] **Error handling:** Malformed JSON, unexpected content types, huge headers — all return clean JSON errors, not stack traces.
- [ ] **ProGuard / R8 rules:** Ensure Ktor and kotlinx-serialization survive minification.
- [ ] **App icon and branding.**

### Verification

1. Turn off Wi-Fi on the AVD — warning appears.
2. Reboot the AVD — service restarts automatically.
3. Send malformed JSON to every endpoint — confirm clean 400 responses, no crashes.
4. Build a release APK — install and verify all features work with minification enabled.

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
