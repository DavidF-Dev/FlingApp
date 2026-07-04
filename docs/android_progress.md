# Android App — Implementation Plan

This is a **live document** tracking the phased implementation of the Fling Android app. Each phase is a vertical slice that can be built, verified, and committed independently. Phases are ordered so that each one builds on the last and is testable in isolation.

**Target AVD:** Pixel_8, API 36

**Test framework:** JUnit 4 + kotlinx-coroutines-test. Ktor routes tested via Ktor's `testApplication` (in-process, no real server). Android-specific code (notifications, clipboard) tested manually on the AVD.

---

## Phase 1: Project Scaffolding & Foreground Service

**Goal:** An Android app that starts a foreground service with a persistent notification showing "Fling is listening on port 7291". No HTTP server yet — just the service lifecycle.

### Tasks

- [x] Create a new Android project (package: `dev.davidfdev.fling`, min SDK 26, target SDK 36).
- [x] Add Gradle dependencies: Jetpack Compose (BOM), Material 3, Ktor (server-netty, content-negotiation, kotlinx-serialization), DataStore Preferences. Note: `kotlin-android` plugin is not needed under AGP 9 (bundled via `kotlin-compose`). Accompanist not needed (Compose BOM handles permissions).
- [x] Set up the test infrastructure: JUnit 4, kotlinx-coroutines-test, Ktor test dependencies in `build.gradle.kts`.
- [ ] Create `FlingService` — a foreground service that:
  - Creates a notification channel (`fling_service`) on start.
  - Posts a persistent notification: "Fling is running". Tapping the notification opens `MainActivity` (brings to front if already open).
  - Runs as `START_STICKY`.
  - Foreground service type: `specialUse`.
- [ ] Create a minimal Compose `MainActivity` with a single button: Start / Stop service.
- [ ] Add `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_SPECIAL_USE`, and `POST_NOTIFICATIONS` permissions to the manifest.
- [ ] Handle the POST_NOTIFICATIONS runtime permission request on API 33+.

### Verification

1. Build and install on Pixel_8 AVD.
2. Tap "Start" — persistent notification appears in the shade.
3. Tap "Stop" — notification disappears.
4. Kill the app from recents — service continues running (START_STICKY).
5. No crashes in Logcat.

---

## Phase 2: Ktor HTTP Server & `/ping`

**Goal:** The foreground service embeds a Ktor HTTP server. The `/ping` endpoint responds to requests.

### Tasks

- [ ] Embed a Ktor Netty server inside `FlingService`, started/stopped with the service lifecycle.
- [ ] Listen on `0.0.0.0:7291`.
- [ ] Install `ContentNegotiation` with `kotlinx.serialization.json`.
- [ ] Implement `GET /ping` — returns `{"status":"ok","name":"<device_model>","version":"1.0.0"}`. No auth check yet.
- [ ] Update the persistent notification to include the device's local IP address (e.g., "Listening on 192.168.1.50:7291").
- [ ] Handle server start failure gracefully (e.g., port already in use) — log and stop the service.

### Unit Tests

- [ ] `GET /ping` returns 200 with correct JSON shape (`status`, `name`, `version`) — via Ktor `testApplication`.
- [ ] Unknown routes return 404 (not a stack trace).

### Verification

1. Start the service on the AVD.
2. Note the IP shown in the notification.
3. From the host PC terminal: `curl http://<ip>:7291/ping`
4. Confirm JSON response with status, name, and version.
5. Stop the service — confirm the server stops accepting connections.

> **Note:** The AVD and host communicate via the emulator's network. Use `adb forward tcp:7291 tcp:7291` if the AVD IP is not directly reachable, then curl `http://localhost:7291/ping`.

---

## Phase 3: DataStore & Pairing (`/pair`)

**Goal:** The phone can accept pairing requests from PCs. Paired device keys are persisted in DataStore.

### Tasks

- [ ] Define a `PairedDevice` data class: `name: String`, `apiKey: String`, `pairedAt: Long`.
- [ ] Create `DeviceRepository` — wraps DataStore to store/retrieve/delete paired devices. Serialize as JSON in a DataStore Preferences string entry.
- [ ] Implement `POST /pair`:
  - Parse request body: `{ "name": "My PC", "key": "<api-key>" }`.
  - If the key is already paired, respond `{"status":"accepted","name":"<phone_name>"}` immediately (idempotent re-pair).
  - Otherwise, suspend the request and prompt the user for approval.
  - On accept: store the device in DataStore, respond `{"status":"accepted","name":"<phone_name>"}`.
  - On reject: respond `{"status":"rejected"}`.
- [ ] Implement the pairing approval mechanism behind a `PairingApprover` interface (`suspend fun requestApproval(deviceName: String): Boolean`):
  - MVP implementation: show an Android notification with Accept/Reject action buttons (PendingIntent → BroadcastReceiver). The Ktor request suspends on a `CompletableDeferred<Boolean>` and resumes when the user taps an action.
  - The route handler calls only the `PairingApprover` interface — it does not know whether approval happens via notification, dialog, or anything else. This makes upgrading the UX later (e.g., full-screen dialog) a single-class swap.
- [ ] Add a timeout: if the user doesn't respond within 30 seconds, auto-reject and respond `{"status":"rejected"}`.

### Unit Tests

- [ ] `POST /pair` with valid body returns 200 and correct JSON shape — via `testApplication` (auto-accept in tests by providing a fake approval mechanism).
- [ ] `POST /pair` with missing `name` or `key` returns 400.
- [ ] `POST /pair` with malformed JSON returns 400.
- [ ] `DeviceRepository` round-trip: store a device, retrieve it, assert fields match (use a test DataStore instance).
- [ ] `DeviceRepository` delete: store, delete, confirm absent.
- [ ] Idempotent re-pair: pair with same key twice, confirm only one device entry stored.

### Verification

1. Start the service.
2. From the PC: `curl -X POST http://<ip>:7291/pair -H "Content-Type: application/json" -d '{"name":"Test PC","key":"abc123"}'`
3. Confirm a notification/dialog appears on the phone asking to approve.
4. Tap Accept — confirm the response is `{"status":"accepted",...}`.
5. Repeat the same curl — confirm idempotent re-pair (accepted immediately, no prompt).
6. Send a new key: `curl ... -d '{"name":"Other PC","key":"xyz789"}'` — tap Reject — confirm `{"status":"rejected"}`.
7. Kill and restart the app — confirm previously paired device key is still in DataStore (re-pair with `abc123` should be idempotent).

---

## Phase 4: API Key Authentication

**Goal:** The `/ping` and future `/clip` endpoints require a valid `X-Fling-Key` header. Unauthenticated requests get `401`.

### Tasks

- [ ] Create a Ktor plugin or route interceptor that:
  - Reads the `X-Fling-Key` header.
  - Looks it up in the `DeviceRepository`.
  - If missing or not found, responds `401 Unauthorized` with `{"error":"unauthorized"}`.
- [ ] Apply the interceptor to `/ping` and `/clip` (when it exists). Exclude `/pair` (pairing is how you get a key).
- [ ] Return a structured 401 JSON body, not a Ktor default HTML error page.

### Unit Tests

- [ ] Request without `X-Fling-Key` header → 401 with JSON body — via `testApplication`.
- [ ] Request with unknown key → 401.
- [ ] Request with valid key → 200 (passes through to the route handler).
- [ ] `/pair` is not affected by the interceptor (no key required).

### Verification

1. Pair a test device (Phase 3).
2. `curl http://<ip>:7291/ping` (no header) → 401.
3. `curl http://<ip>:7291/ping -H "X-Fling-Key: wrong"` → 401.
4. `curl http://<ip>:7291/ping -H "X-Fling-Key: abc123"` → 200 with ping response.

---

## Phase 5: Receive Clipboard Content (`/clip`)

**Goal:** The phone can receive text and image content via `POST /clip` and store it in an in-memory buffer.

### Tasks

- [ ] Define a `ClipItem` data class: `type: String` (MIME), `data: ByteArray` (decoded from base64), `timestamp: Long`, `receivedAt: Long`.
- [ ] Create `ClipboardBuffer` — an in-memory ring buffer holding the last 10 items. Thread-safe.
- [ ] Implement `POST /clip`:
  - Requires `X-Fling-Key` authentication (Phase 4 interceptor).
  - Parse request body: `{ "type": "text/plain", "data": "<base64>", "timestamp": 1720100000 }`.
  - Validate `type` is one of: `text/plain`, `text/html`, `image/png`.
  - Decode `data` from base64.
  - If `"compressed": true` in the JSON body, gunzip the decoded bytes (for text types). This is application-level compression — the HTTP `Content-Encoding` header is not used.
  - Enforce max payload size (default 10 MB on the decoded content). Return `413` if exceeded.
  - Add to `ClipboardBuffer`.
  - Respond `{"status":"ok"}`.
- [ ] Return appropriate error responses:
  - `400` for malformed JSON or unsupported type.
  - `413` for oversized payloads.

### Unit Tests

- [ ] `POST /clip` with valid text payload → 200, item added to buffer — via `testApplication`.
- [ ] `POST /clip` with valid `image/png` payload → 200.
- [ ] Unsupported MIME type → 400.
- [ ] Malformed JSON → 400.
- [ ] Payload exceeding max size → 413.
- [ ] Missing required fields (`type`, `data`) → 400.
- [ ] GZip-encoded text: send with `"compressed": true`, verify gunzipped content matches original.
- [ ] `ClipboardBuffer`: add items, verify FIFO eviction at capacity (ring buffer behavior).
- [ ] `ClipboardBuffer`: thread safety — concurrent writes don't corrupt state (use `runBlocking` + coroutines).

### Verification

1. Pair a device.
2. Send plain text:
   ```
   curl -X POST http://<ip>:7291/clip \
     -H "X-Fling-Key: abc123" \
     -H "Content-Type: application/json" \
     -d '{"type":"text/plain","data":"SGVsbG8gV29ybGQ=","timestamp":1720100000}'
   ```
   Confirm `{"status":"ok"}`.
3. Send with a bad key → 401.
4. Send with an unsupported type (e.g., `application/pdf`) → 400.
5. Send an oversized payload → 413.
6. (Buffer verification happens via UI in Phase 7 or via logs for now.)

---

## Phase 6: Notifications & Tap-to-Copy

**Goal:** When content arrives, a notification appears. Tapping the notification copies the content to the phone's clipboard.

### Tasks

- [ ] Create a second notification channel (`fling_content`) for received content notifications.
- [ ] When a `ClipItem` is added to the buffer, post a notification:
  - **Text:** Show a preview (first ~100 characters, truncated).
  - **Image:** Show the image as a `BigPictureStyle` notification thumbnail.
  - Each notification gets a unique ID (use a counter or timestamp).
- [ ] On notification tap (PendingIntent → BroadcastReceiver or Activity):
  - Write the content to the system clipboard (`ClipboardManager`).
  - For `text/plain` and `text/html`: set as `ClipData.newPlainText` / `newHtmlText`.
  - For `image/png`: write to a temporary `FileProvider` URI and set as `ClipData.newUri`.
  - Show a toast: "Copied to clipboard".
  - Dismiss the notification.
- [ ] Auto-expire notifications after 5 minutes (use `setTimeoutAfter` on the notification builder).

### Verification

1. Send text via curl (Phase 5 test).
2. Confirm a notification appears with a text preview.
3. Tap the notification → open any text field → paste → confirm the sent text appears.
4. Send an image (base64-encode a small PNG, send as `image/png`).
5. Confirm a notification with image thumbnail appears.
6. Tap → paste into a messaging app → confirm image pastes.
7. Wait 5 minutes (or lower the timeout for testing) → confirm notification auto-dismisses.

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
  - **Top section:** Service status toggle (listening / stopped), IP and port display.
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
