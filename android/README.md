# Fling - Android App

The Android half of [Fling](../README.md). Runs an embedded HTTP server in a foreground
service to receive clipboard content from paired PCs over the local network.

## Requirements

- Android 8.0 (API 26) or newer.

## Building

Requires JDK 17 (Android Studio's bundled JDK works). From the `android/` directory:

```
./gradlew assembleDebug      # debug APK
./gradlew test                # JVM unit tests
./gradlew assembleRelease     # release APK (see Signing)
```

### Signing a release

Release builds are signed from a git-ignored `keystore.properties`. Copy
`keystore.properties.template` to `keystore.properties` and fill it in; generate the
keystore with:

```
keytool -genkeypair -v -keystore fling-release.jks -alias fling \
  -keyalg RSA -keysize 2048 -validity 10000
```

Without `keystore.properties`, `assembleRelease` falls back to debug signing so the build
stays runnable for testing. Never commit the keystore or its passwords.

### Installing on a device

```
powershell -File scripts/install-device.ps1                  # release, USB device
powershell -File scripts/install-device.ps1 -DebugBuild      # debug variant
powershell -File scripts/install-device.ps1 -Forward          # also adb forward port 7291
```

## License

See [LICENSE](../LICENSE).

App id: `dev.davidfdev.fling`
