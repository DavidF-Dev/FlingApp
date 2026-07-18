<#
.SYNOPSIS
    Build, sign, and publish a Fling Android release to GitHub Releases.

.DESCRIPTION
    Reads the version from app/build.gradle.kts (single source of truth), checks guard
    rails, builds the signed release APK, then — after explicit confirmation — creates the
    GitHub Release with the APK attached and the matching CHANGELOG.md section as the body.
    The signing key stays local; nothing is published until you confirm.

    Prerequisites: gh CLI installed and authenticated (gh auth login), and a real
    keystore.properties present so the build is release-signed (not debug-signed).

    Before running: bump versionName (and versionCode) in app/build.gradle.kts and write
    the matching CHANGELOG.md "## [x.y.z]" section, then commit.

.PARAMETER Force
    Skip the confirmation prompt (for non-interactive use). Off by default.

.EXAMPLE
    powershell -File android/scripts/release.ps1
#>
[CmdletBinding()]
param([switch]$Force)

# ErrorActionPreference is deliberately left as Continue: Stop turns native-command
# stderr (git/gh write status there even on success) into terminating errors on Windows
# PowerShell. Guard rails check exit codes explicitly instead.

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    function Fail([string]$message) {
        Write-Host "release: $message" -ForegroundColor Red
        exit 1
    }

    # --- Version: single source of truth is versionName in the build script ---
    $match = [regex]::Match((Get-Content 'app/build.gradle.kts' -Raw -Encoding UTF8), 'versionName\s*=\s*"(.+?)"')
    if (-not $match.Success) { Fail 'could not find versionName in app/build.gradle.kts' }
    $version = $match.Groups[1].Value
    $tag = "android/v$version"

    # --- Guard rails: fail before building anything ---
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Fail 'gh CLI not found — install it, then run: gh auth login'
    }
    gh auth status 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'gh is not authenticated — run: gh auth login' }

    if (git status --porcelain) { Fail 'working tree is dirty — commit or stash changes first' }

    git rev-parse --symbolic-full-name '@{u}' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'current branch has no upstream — push it first (git push -u origin HEAD)' }
    if ([int](git rev-list --count '@{u}..HEAD') -ne 0) { Fail 'HEAD is ahead of the remote — push first (git push)' }

    if (-not (Test-Path 'keystore.properties')) {
        Fail 'keystore.properties is missing — the build would be debug-signed'
    }

    if (git tag --list $tag) { Fail "tag $tag already exists" }
    gh release view $tag 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Fail "release $tag already exists" }

    # --- Extract this version's CHANGELOG section (everything under "## [x.y.z]") ---
    $notes = & {
        $body = @()
        $inSection = $false
        foreach ($line in (Get-Content 'CHANGELOG.md' -Encoding UTF8)) {
            if ($line -match '^##\s+\[(.+?)\]') {
                if ($inSection) { break }
                if ($Matches[1] -eq $version) { $inSection = $true; continue }
            } elseif ($inSection) {
                $body += $line
            }
        }
        ($body -join "`n").Trim()
    }
    if ([string]::IsNullOrWhiteSpace($notes)) { Fail "no CHANGELOG.md section found for [$version]" }

    # --- Cross-reference: latest CLI release tag ---
    $crossRef = ''
    $cliTag = (git tag -l 'cli/v*' --sort=-v:refname | Select-Object -First 1)
    if ($cliTag) {
        $repoUrl = 'https://github.com/DavidF-Dev/FlingApp'
        $crossRef = "Compatible with: [$cliTag]($repoUrl/releases/tag/$cliTag)"
    }

    # --- Build the signed release APK ---
    $env:JAVA_HOME = 'C:\Program Files\Android\Android Studio\jbr'
    Write-Host "Building signed release APK for $tag ..." -ForegroundColor Cyan
    & .\gradlew.bat assembleRelease
    if ($LASTEXITCODE -ne 0) { Fail 'assembleRelease failed' }
    $apk = 'app/build/outputs/apk/release/fling-release.apk'
    if (-not (Test-Path $apk)) { Fail "expected APK not found at $apk" }
    $sha = (Get-FileHash $apk -Algorithm SHA256).Hash
    $apkSizeMb = [math]::Round((Get-Item $apk).Length / 1MB, 2)

    # --- Compose release body ---
    $assetName = "fling-$version.apk"
    $releaseBody = $notes
    if ($crossRef) {
        $releaseBody += "`n`n$crossRef"
    }
    $releaseBody += "`n`n---`nSHA-256 ($assetName): ``$sha``"

    # --- Confirm before the outward, irreversible step (tag + publish) ---
    Write-Host ''
    Write-Host 'About to publish a GitHub Release:' -ForegroundColor Yellow
    Write-Host "  Tag / title : $tag  (gh creates the tag at HEAD)"
    Write-Host "  APK         : $assetName  (from $apk)"
    Write-Host "  Size        : $apkSizeMb MB"
    Write-Host "  SHA-256     : $sha"
    Write-Host '  Notes       :'
    $releaseBody -split "`n" | ForEach-Object { Write-Host "      $_" }
    Write-Host ''
    if (-not $Force) {
        if ((Read-Host "Type 'yes' to create and publish this release") -ne 'yes') {
            Write-Host 'Aborted — nothing was published.' -ForegroundColor Yellow
            exit 0
        }
    }

    # --- Publish ---
    $notesFile = New-TemporaryFile
    [System.IO.File]::WriteAllText($notesFile.FullName, $releaseBody, (New-Object System.Text.UTF8Encoding($false)))
    $asset = Join-Path ([System.IO.Path]::GetTempPath()) $assetName
    Copy-Item $apk $asset -Force
    gh release create $tag $asset --title "Fling Android v$version" --notes-file $notesFile.FullName
    $published = ($LASTEXITCODE -eq 0)
    Remove-Item $notesFile -ErrorAction SilentlyContinue
    Remove-Item $asset -ErrorAction SilentlyContinue
    if (-not $published) { Fail 'gh release create failed' }
    Write-Host "Published $tag ($assetName)" -ForegroundColor Green
}
finally {
    Pop-Location
}
