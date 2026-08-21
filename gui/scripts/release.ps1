<#
.SYNOPSIS
    Build and publish a Fling tray app release to GitHub Releases.

.DESCRIPTION
    Reads the version from src/Fling.Gui/Fling.Gui.csproj (single source of truth), checks
    guard rails, builds the self-contained zip via publish.ps1, then (after explicit
    confirmation) creates the GitHub Release with the zip attached and the matching
    CHANGELOG.md section as the body. Nothing is published until you confirm.

    Prerequisites: gh CLI installed and authenticated (gh auth login).

    Before running: bump <Version> in src/Fling.Gui/Fling.Gui.csproj and write the matching
    CHANGELOG.md "## [x.y.z]" section, then commit.

.PARAMETER Force
    Skip the confirmation prompt (for non-interactive use). Off by default.

.EXAMPLE
    powershell -File gui/scripts/release.ps1
#>
[CmdletBinding()]
param([switch]$Force)

# ErrorActionPreference is deliberately left as Continue: see ReleaseCommon.ps1.

. (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'scripts\ReleaseCommon.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    # --- Version: single source of truth is <Version> in the csproj ---
    $csproj = 'src/Fling.Gui/Fling.Gui.csproj'
    $match = [regex]::Match((Get-Content $csproj -Raw -Encoding UTF8), '<Version>(.+?)</Version>')
    if (-not $match.Success) { Fail "could not find <Version> in $csproj" }
    $version = $match.Groups[1].Value
    $tag = "gui/v$version"

    Assert-ReleasePreconditions -Tag $tag

    $notes = Get-ChangelogSection -Path 'CHANGELOG.md' -Version $version

    # The tray app talks to the phone and shares configuration with the CLI, so both are
    # worth naming.
    $compatibility = Get-CompatibilityNote -TagPatterns @('android/v*', 'cli/v*')

    # --- Build the distributable archive (publish.ps1 runs the tests and packages) ---
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish.ps1')
    if ($LASTEXITCODE -ne 0) { Fail 'publish.ps1 failed' }

    $assetName = "fling-tray-$version-win-x64.zip"
    $asset = "dist/$assetName"
    if (-not (Test-Path $asset)) { Fail "expected $asset not found" }

    $sha    = (Get-FileHash $asset -Algorithm SHA256).Hash
    $sizeMb = [math]::Round((Get-Item $asset).Length / 1MB, 1)

    $body = New-ReleaseBody -Notes $notes -AssetName $assetName -Sha256 $sha -CompatibilityNote $compatibility

    if (-not (Confirm-Release -Tag $tag -AssetName $assetName -Sha256 $sha -Body $body -SizeMb $sizeMb -Force:$Force)) {
        exit 0
    }

    Publish-Release -Tag $tag -Title "Fling Tray v$version" -AssetPath $asset -Body $body
}
finally {
    Pop-Location
}
