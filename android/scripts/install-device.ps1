<#
.SYNOPSIS
    Build Fling and install it on a connected physical device.

.DESCRIPTION
    Builds the release APK (the default; pass -DebugBuild for debug), selects the single
    USB-connected physical device (any running emulator is ignored), prints a summary, and -
    after confirmation - installs it. By default it then grants POST_NOTIFICATIONS and
    launches the app.

    adb is resolved from ANDROID_HOME, ANDROID_SDK_ROOT, the default SDK location, or PATH.
    The device must have USB debugging enabled and be authorised.

.PARAMETER DebugBuild
    Build and install the debug variant instead of release.

.PARAMETER NoBuild
    Skip the build and install the existing APK for the chosen variant. Fails if no such
    APK is present.

.PARAMETER Reinstall
    Uninstall the app first, then install. Needed when the installed build has a different
    signature (e.g. switching between release and debug). This WIPES the app's data.

.PARAMETER NoLaunch
    Skip granting POST_NOTIFICATIONS and launching the app after install.

.PARAMETER Forward
    Run adb forward tcp:7291 tcp:7291 after install, so the PC can reach the Fling server
    via localhost.

.PARAMETER Force
    Skip the confirmation prompt.

.EXAMPLE
    powershell -File scripts/install-device.ps1

.EXAMPLE
    powershell -File scripts/install-device.ps1 -DebugBuild -Reinstall -Forward
#>
[CmdletBinding()]
param(
    [switch]$DebugBuild,
    [switch]$NoBuild,
    [switch]$Reinstall,
    [switch]$NoLaunch,
    [switch]$Forward,
    [switch]$Force
)

$package = 'dev.davidfdev.fling'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    function Fail([string]$message) {
        Write-Host "install-device: $message" -ForegroundColor Red
        exit 1
    }

    # --- Resolve adb ---
    $adb = $null
    foreach ($base in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'))) {
        if ($base) {
            $candidate = Join-Path $base 'platform-tools\adb.exe'
            if (Test-Path $candidate) { $adb = $candidate; break }
        }
    }
    if (-not $adb) {
        $onPath = Get-Command adb -ErrorAction SilentlyContinue
        if ($onPath) { $adb = $onPath.Source }
    }
    if (-not $adb) { Fail 'adb not found - set ANDROID_HOME or install the SDK platform-tools' }

    # --- Select the single physical device (emulators are excluded by their serial) ---
    $devices = @()
    foreach ($line in (& $adb devices)) {
        if ($line -match '^(\S+)\s+(device|unauthorized|offline)\b') {
            $devices += [pscustomobject]@{ Serial = $Matches[1]; State = $Matches[2] }
        }
    }
    $physical = @($devices | Where-Object { $_.Serial -notmatch '^emulator-' })
    $ready = @($physical | Where-Object { $_.State -eq 'device' })
    if ($ready.Count -eq 0) {
        if ($physical | Where-Object { $_.State -eq 'unauthorized' }) {
            Fail 'a physical device is connected but unauthorised - accept the USB-debugging prompt on it'
        }
        Fail 'no physical device detected - connect one via USB with USB debugging enabled'
    }
    if ($ready.Count -gt 1) {
        $list = ($ready | ForEach-Object { $_.Serial }) -join ', '
        Fail "more than one physical device attached ($list) - leave only one connected"
    }
    $serial = $ready[0].Serial
    $model = (& $adb -s $serial shell getprop ro.product.model | Out-String).Trim()

    # --- Build ---
    if ($DebugBuild) {
        $task = 'assembleDebug'; $apk = 'app/build/outputs/apk/debug/fling-debug.apk'; $variant = 'debug'
    } else {
        $task = 'assembleRelease'; $apk = 'app/build/outputs/apk/release/fling-release.apk'; $variant = 'release'
    }
    if ($NoBuild) {
        if (-not (Test-Path $apk)) { Fail "no existing $variant APK at $apk - build one first or drop -NoBuild" }
        Write-Host "Using existing $variant APK (skipping build) ..." -ForegroundColor Cyan
    } else {
        Write-Host "Building $variant APK ..." -ForegroundColor Cyan
        & .\gradlew.bat $task
        if ($LASTEXITCODE -ne 0) { Fail "$task failed" }
        if (-not (Test-Path $apk)) { Fail "expected APK not found at $apk" }
    }

    $version = ([regex]::Match((Get-Content 'app/build.gradle.kts' -Raw -Encoding UTF8), 'versionName\s*=\s*"(.+?)"')).Groups[1].Value
    $apkItem = Get-Item $apk
    $apkSizeMb = [math]::Round($apkItem.Length / 1MB, 2)
    $apkBuilt = $apkItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm')

    # --- Confirm ---
    Write-Host ''
    Write-Host 'About to install on a physical device:' -ForegroundColor Yellow
    Write-Host "  App / version : Fling $version"
    Write-Host "  Variant       : $variant"
    Write-Host "  APK           : $apk  ($apkSizeMb MB, built $apkBuilt)"
    Write-Host "  Device        : $model ($serial)"
    if ($Reinstall) { Write-Host '  Mode          : uninstall first - app data WILL be wiped' -ForegroundColor Yellow }
    Write-Host ''
    if (-not $Force) {
        if ((Read-Host "Type 'yes' to install") -ne 'yes') {
            Write-Host 'Aborted - nothing was installed.' -ForegroundColor Yellow
            exit 0
        }
    }

    # --- Install ---
    if ($Reinstall) {
        & $adb -s $serial uninstall $package | Out-Null
        $out = (& $adb -s $serial install $apk 2>&1 | Out-String)
    } else {
        $out = (& $adb -s $serial install -r $apk 2>&1 | Out-String)
    }
    if ($LASTEXITCODE -ne 0 -or $out -match 'Failure|INSTALL_FAILED') {
        if ($out -match 'INSTALL_FAILED_UPDATE_INCOMPATIBLE|signatures do not match') {
            Fail 'signature mismatch with the installed build - re-run with -Reinstall (wipes app data)'
        }
        Fail "install failed: $($out.Trim())"
    }
    Write-Host "Installed Fling $version ($variant) on $model" -ForegroundColor Green

    # --- Grant notifications + launch ---
    if (-not $NoLaunch) {
        & $adb -s $serial shell pm grant $package android.permission.POST_NOTIFICATIONS 2>$null | Out-Null
        & $adb -s $serial shell am start -n "$package/.MainActivity" | Out-Null
        Write-Host 'Launched on device.' -ForegroundColor Green
    }

    # --- Port forwarding ---
    if ($Forward) {
        & $adb -s $serial forward tcp:7291 tcp:7291 | Out-Null
        Write-Host 'Port forwarding: localhost:7291 -> device:7291' -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
