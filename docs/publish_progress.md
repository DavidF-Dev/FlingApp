# Publish & Release — Implementation Plan

This is a **live document** tracking the phased implementation of the Fling release pipeline. Each component (CLI, Android, tray app) releases independently with its own version, changelog, and GitHub Release.

**Reference implementations:**
- Yohaku: `publish.ps1` (build) + `release.ps1` (guard rails, changelog, `gh release create`). Single-component .NET app.
- StelaApp: `release.ps1` (combined build + publish). Single-component Android app with release signing.

**Conventions (shared across all components):**

- **Independent versioning.** Each component has its own semver version and changelog.
- **Tag format:** `cli/v*`, `android/v*`, `gui/v*` — named for the directory each component lives in.
- **Changelog format:** [Keep a Changelog](https://keepachangelog.com/). Release scripts extract the matching `## [x.y.z]` section for the GitHub Release body.
- **Cross-references.** Each release description includes a "Compatible with" line linking to the latest release of the other component(s), auto-detected from git tags.
- **Guard rails.** Every release script checks: `gh` installed and authenticated, working tree clean, HEAD pushed, tag doesn't exist, changelog section exists.
- **Confirmation prompt.** The script shows what it's about to publish and waits for explicit `yes` before creating the tag and release. `-Force` skips the prompt for non-interactive use.
- **Version source of truth.** CLI: `<Version>` in `cli/src/Fling/Fling.csproj`. Tray app: `<Version>` in `gui/src/Fling.Gui/Fling.Gui.csproj`. Android: `versionName` in `android/app/build.gradle.kts`.
- **Shared implementation.** The two .NET release scripts dot-source `scripts/ReleaseCommon.ps1` for the guard rails, changelog extraction, cross-references, confirmation, and publish. The Android script is deliberately left standalone: it is a different toolchain, and it is the one release path that cannot be rehearsed without publishing.

---

## Phase 1: CLI Release Script ✅

**Goal:** `cli/scripts/release.ps1` creates a GitHub Release with the zip attached.

**Context:** `cli/scripts/publish.ps1` already builds `fling-<version>-win-x64.zip` in `dist/`. The release script wraps it with guard rails, changelog extraction, and `gh release create`.

### Prerequisites

- [x] Create `cli/CHANGELOG.md` using Keep a Changelog format.
- [x] Populate the `## [1.0.0]` section.

### Tasks

- [x] Create `cli/scripts/release.ps1`:
  1. Read version from `cli/src/Fling/Fling.csproj` (`<Version>` tag).
  2. Compute tag: `cli/v$version`.
  3. Guard rails:
     - `gh` CLI installed and authenticated.
     - Working tree is clean (`git status --porcelain` is empty).
     - HEAD is pushed to remote (local and remote SHA match).
     - Tag `cli/v$version` does not already exist (local or on GitHub).
  4. Extract the `## [$version]` section from `cli/CHANGELOG.md`. Fail if not found.
  5. Auto-detect the latest `android/v*` tag for the cross-reference line.
  6. Call `cli/scripts/publish.ps1` to build the zip.
  7. Compose the release body: changelog notes + SHA-256 + "Compatible with" cross-reference.
  8. Print summary: tag, asset filename, size, SHA-256, release body.
  9. Prompt for confirmation (unless `-Force`).
  10. Create the GitHub Release via `gh release create` with the zip attached.
- [x] Title format: `Fling CLI v$version` (e.g., "Fling CLI v1.0.0").

### Design decisions

- **Separate publish + release scripts (Yohaku pattern).** `publish.ps1` is useful on its own for local builds; `release.ps1` adds the GitHub-facing steps. This is preferred over a combined script (StelaApp pattern) because the CLI publish step is already implemented.
- **Tag is created by `gh release create`** — no separate `git tag` step needed. `gh` creates the tag at HEAD automatically.
- **Notes via temp file.** Use `[System.IO.File]::WriteAllText` with UTF-8 no BOM (as StelaApp does) to avoid encoding issues on Windows PowerShell.

### Verification

1. `cli/scripts/release.ps1` with a dirty tree → fails with clear message.
2. `cli/scripts/release.ps1` with missing changelog section → fails.
3. `cli/scripts/release.ps1` with duplicate tag → fails.
4. Successful run → GitHub Release created with zip, changelog body, SHA-256, and cross-reference.

---

## Phase 2: Android Release Signing ✅

**Goal:** The Android app can produce a release-signed APK suitable for distribution.

**Context:** The Android build currently has no `signingConfigs` block and no `keystore.properties`. Without release signing, `assembleRelease` produces an unsigned APK that can't be installed without `adb`.

### Prerequisites

- [x] Generate a release keystore (`.jks` file). Store securely outside the repo.
- [x] Create `android/keystore.properties` (gitignored) with keystore path, alias, and passwords.

### Tasks

- [x] Add `signingConfigs` block to `android/app/build.gradle.kts`:
  - Read keystore path, alias, store password, and key password from `keystore.properties`.
  - Apply the signing config to the `release` build type.
- [x] Add `keystore.properties` to `android/.gitignore`.
- [x] Verify `./gradlew assembleRelease` produces a signed APK.
- [x] Record the signing certificate's SHA-256 fingerprint for use in release notes.

### Design decisions

- **`keystore.properties` pattern (StelaApp pattern).** The release script checks for this file before building and fails with a clear message if missing. Keeps secrets out of the repo.
- **`base.archivesName` is already set** to `"fling"` in build.gradle.kts, so the APK output will be `fling-release.apk`.

### Verification

1. `./gradlew assembleRelease` without `keystore.properties` → build fails or produces unsigned APK.
2. `./gradlew assembleRelease` with `keystore.properties` → signed `fling-release.apk` in `app/build/outputs/apk/release/`.
3. `apksigner verify --print-certs fling-release.apk` → shows the signing certificate.

---

## Phase 3: Android Release Script ✅

**Goal:** `android/scripts/release.ps1` builds a signed APK and creates a GitHub Release.

**Context:** Unlike the CLI (which has a separate `publish.ps1`), the Android build is a single Gradle command. Following the StelaApp pattern, the release script handles both building and publishing.

### Prerequisites

- [x] Phase 2 complete (release signing works).
- [x] Create `android/CHANGELOG.md` using Keep a Changelog format.
- [x] Populate the `## [1.0.0]` section.

### Tasks

- [x] Create `android/scripts/release.ps1`:
  1. Read version from `android/app/build.gradle.kts` (`versionName`).
  2. Compute tag: `android/v$version`.
  3. Guard rails:
     - `gh` CLI installed and authenticated.
     - Working tree is clean.
     - HEAD is pushed to remote.
     - Tag `android/v$version` does not already exist.
     - `keystore.properties` exists (fail with clear message if missing).
  4. Extract the `## [$version]` section from `android/CHANGELOG.md`. Fail if not found.
  5. Auto-detect the latest `cli/v*` tag for the cross-reference line.
  6. Set `JAVA_HOME` to Android Studio's bundled JBR (as StelaApp does).
  7. Run `./gradlew assembleRelease`.
  8. Locate the signed APK at `app/build/outputs/apk/release/fling-release.apk`.
  9. Compose the release body: changelog notes + APK SHA-256 fingerprint + "Compatible with" cross-reference.
  10. Print summary: tag, asset filename, size, release body.
  11. Prompt for confirmation (unless `-Force`).
  12. Stage a version-stamped copy (`fling-<version>.apk`) and create the GitHub Release via `gh release create`.
- [x] Title format: `Fling Android v$version` (e.g., "Fling Android v1.0.0").

### Design decisions

- **Combined build + release script (StelaApp pattern).** No separate `publish.ps1` — the Gradle build is a single command and doesn't need a standalone wrapper.
- **Asset renamed at upload time.** `fling-release.apk` → `fling-1.0.0.apk` for the GitHub Release asset (download name = file basename). Staged in temp, cleaned up after.
- **`JAVA_HOME` set explicitly.** Android Studio's bundled JBR avoids JDK version mismatches. Path may need to be configurable or detected.

### Verification

1. Guard rails reject: dirty tree, missing changelog section, duplicate tag, missing keystore.
2. Successful run → GitHub Release created with `fling-<version>.apk`, changelog body, fingerprint, and cross-reference.

---

## Phase 4: Cross-Reference Automation ✅ (folded into Phases 1 and 3)

**Goal:** Both release scripts automatically include "Compatible with" links to the latest release of the other component.

**Context:** Cross-referencing is implemented inline in each release script rather than as a shared helper. The CLI release script detects the latest `android/v*` tag; the Android script will detect the latest `cli/v*` tag.

### Tasks

- [x] Implement cross-reference logic inline in each release script:
  1. Runs `git tag -l "<component>/v*" --sort=-v:refname` to find the latest tag for a given component prefix.
  2. Formats the cross-reference: `Compatible with: [cli/v0.1.0](https://github.com/DavidF-Dev/FlingApp/releases/tag/cli/v0.1.0)`.
  3. If no tag exists for the other component (first release), omits the line gracefully.
- [x] Include the cross-reference in the release body, after the changelog notes and SHA-256.

### Design decisions

- **Git tags as source of truth** for the latest compatible version. No parsing of changelog files or version strings from build configs of the other component.
- **Informational, not enforced.** The cross-reference tells users what was current at release time. It doesn't block releases if the other component hasn't been released yet.

### Verification

1. CLI release when no `android/v*` tag exists → release body omits the cross-reference line.
2. CLI release when `android/v1.0.0` exists → release body includes `Compatible with: [android/v1.0.0](...)`.
3. Same for Android releasing with/without CLI tags.

---

## Appendix: Release Workflow Checklist

For each component release, the developer should:

1. Bump the version in the source of truth (csproj or build.gradle.kts).
2. Update `versionCode` (Android only — must increment for each Play Store upload).
3. Write the `## [x.y.z]` section in the component's `CHANGELOG.md`.
4. Commit the version bump and changelog.
5. Run the release script: `powershell -File <component>/scripts/release.ps1`.
6. Verify the GitHub Release page looks correct.
