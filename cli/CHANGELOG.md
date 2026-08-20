# Changelog

## [Unreleased]

## [1.0.1] - 2026-08-20

### Fixed

- `send --all` failed with an `InvalidOperationException` when two or more devices were paired.
- Concurrent config writes could discard a device paired by another process.

### Changed

- `fling.exe` is 15.9 MB, down from 69.0 MB. Cold start 618 ms (was 1445 ms); warm start 151 ms (was 204 ms).
- PNG files are sent as-is instead of being decoded and re-encoded.
- Clipboard reading no longer depends on WinForms; format precedence and CF_HTML handling are unchanged.
- Unrecognised settings in `config.json` are preserved rather than dropped when the file is rewritten.

### Internal

- Split into `Fling.Core`, `Fling.Windows`, and the `Fling` CLI ahead of the tray app.

## [1.0.0] - 2026-07-18

First public release.
