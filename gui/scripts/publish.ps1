<#
.SYNOPSIS
    Build a distributable, self-contained FlingTray.exe.

.DESCRIPTION
    Publishes a self-contained, single-file, compressed win-x64 build and packages it as
    dist/fling-tray-<version>-win-x64.zip (version read from src/Fling.Gui/Fling.Gui.csproj,
    the single source of truth). Prints the SHA-256 so a release can advertise it. Runs the
    unit tests first unless -SkipTests.

    The result needs no .NET runtime installed; it is a bare exe meant to be run as-is.

    Unlike the CLI, the tray app is neither trimmed nor subsystem-patched: WPF is not
    trim-compatible, and a WPF application is already a GUI-subsystem binary.

.PARAMETER SkipTests
    Skip the unit-test gate (off by default).

.EXAMPLE
    .\scripts\publish.ps1
#>
[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Host "publish: $message" -ForegroundColor Red
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj   = Join-Path $repoRoot 'src\Fling.Gui\Fling.Gui.csproj'
$sln      = Join-Path $repoRoot 'Fling.Gui.slnx'

# Version: single source of truth is <Version> in the csproj.
$match = [regex]::Match((Get-Content $csproj -Raw -Encoding UTF8), '<Version>(.+?)</Version>')
if (-not $match.Success) { Fail "could not find <Version> in $csproj" }
$version = $match.Groups[1].Value

# A running instance holds a lock on the executable, and the build failure it causes is
# easy to mistake for a code problem.
if (Get-Process FlingTray -ErrorAction SilentlyContinue) {
    Fail 'FlingTray is running — quit it from the tray icon first'
}

if (-not $SkipTests) {
    Write-Host 'Running unit tests...' -ForegroundColor Cyan
    dotnet test $sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { Fail 'unit tests failed' }
}

$publishDir = Join-Path $repoRoot 'src\Fling.Gui\bin\publish'
$distDir    = Join-Path $repoRoot 'dist'

# Clean the publish dir so no stale output can be mistaken for this build.
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "Publishing self-contained single-file build for v$version..." -ForegroundColor Cyan
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish failed' }

$exe = Join-Path $publishDir 'FlingTray.exe'
if (-not (Test-Path $exe)) { Fail "expected $exe not found after publish" }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# Bundle supporting files (LICENSE renamed to .txt for Windows double-click).
$license = Join-Path $repoRoot '..\LICENSE'
if (-not (Test-Path $license)) { Fail 'LICENSE not found at repo root' }
Copy-Item $license (Join-Path $publishDir 'LICENSE.txt') -Force
Copy-Item (Join-Path $repoRoot 'packaging\README.txt') (Join-Path $publishDir 'README.txt') -Force

$zipName = "fling-tray-$version-win-x64.zip"
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
$zipItems = @(
    $exe,
    (Join-Path $publishDir 'LICENSE.txt'),
    (Join-Path $publishDir 'README.txt')
)
Compress-Archive -Path $zipItems -DestinationPath $zipPath

# Also copy the bare exe to dist for local use.
Copy-Item $exe (Join-Path $distDir 'FlingTray.exe') -Force

$sha    = (Get-FileHash $zipPath -Algorithm SHA256).Hash
$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
$exeMb  = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ''
Write-Host "Built: $zipPath" -ForegroundColor Green
Write-Host "  exe size : $exeMb MB"
Write-Host "  zip size : $sizeMb MB"
Write-Host "  SHA-256  : $sha"
