# Changelog

## [Unreleased]

## [1.0.1] - 2026-08-20

### Fixed

- Copy and Share on a received-content notification could act on an earlier fling instead of the one shown. Cached images are now stored under a name unique to each clip, and every notification action is tagged with that clip's identity, so the two can no longer be crossed.
- Received images are no longer cached indefinitely. They are cleared when the service starts, and when a clip is cleared or removed in the app.
- "Copied to clipboard" is no longer shown when nothing was copied. A clip whose content has expired now says so instead of failing silently.
- Copy and Share run in the foreground, so the clipboard write is no longer dropped by background restrictions.
- Clearing one clip no longer removed other clips received in the same millisecond.
- Starting the service more than once no longer left duplicate listeners behind or started a second server that could not bind its port.

## [1.0.0] - 2026-07-18

First public release.
