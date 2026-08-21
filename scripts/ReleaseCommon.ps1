<#
.SYNOPSIS
    Shared helpers for the per-component release scripts.

.DESCRIPTION
    Each component releases independently under its own tag prefix, with its own version
    and CHANGELOG, but the surrounding ceremony is identical: check the tooling and the
    working tree, pull the release notes out of the CHANGELOG, show what is about to
    happen, and publish only after the operator confirms.

    Dot-source this from a component script, which supplies the version, the tag, and the
    built artifact.

    ErrorActionPreference is deliberately left as Continue by callers: Stop turns
    native-command stderr (git and gh write status there even on success) into
    terminating errors on Windows PowerShell. These helpers check exit codes explicitly.
#>

$RepoUrl = 'https://github.com/DavidF-Dev/FlingApp'

function Fail([string]$message) {
    Write-Host "release: $message" -ForegroundColor Red
    exit 1
}

<#
.SYNOPSIS
    Refuses to go further unless the tooling, the working tree, and the remote are all in
    a state where publishing would be reproducible.
#>
function Assert-ReleasePreconditions {
    param(
        [Parameter(Mandatory)][string]$Tag
    )

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Fail 'gh CLI not found — install it, then run: gh auth login'
    }

    gh auth status 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'gh is not authenticated — run: gh auth login' }

    if (git status --porcelain) { Fail 'working tree is dirty — commit or stash changes first' }

    # gh release create makes the tag on the remote only, so a release published from
    # here — or from another machine — leaves no local tag behind. Without this, the
    # duplicate-tag check and the compatibility cross-reference both work from a stale
    # view: releasing two components back to back would name the older of the pair.
    git fetch --tags --quiet 2>$null
    if ($LASTEXITCODE -ne 0) { Fail 'could not fetch tags from the remote' }

    git rev-parse --symbolic-full-name '@{u}' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'current branch has no upstream — push it first (git push -u origin HEAD)' }
    if ([int](git rev-list --count '@{u}..HEAD') -ne 0) { Fail 'HEAD is ahead of the remote — push first (git push)' }

    if (git tag --list $Tag) { Fail "tag $Tag already exists" }
    gh release view $Tag 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Fail "release $Tag already exists" }
}

<#
.SYNOPSIS
    Returns everything under the "## [x.y.z]" heading for this version.
#>
function Get-ChangelogSection {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version
    )

    $body = @()
    $inSection = $false
    foreach ($line in (Get-Content $Path -Encoding UTF8)) {
        if ($line -match '^##\s+\[(.+?)\]') {
            if ($inSection) { break }
            if ($Matches[1] -eq $Version) { $inSection = $true; continue }
        } elseif ($inSection) {
            $body += $line
        }
    }

    $notes = ($body -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($notes)) { Fail "no $Path section found for [$Version]" }
    return $notes
}

<#
.SYNOPSIS
    Names the newest release of each other component, so a reader can tell what this one
    was built against.

.DESCRIPTION
    Versions are not aligned across components, so compatibility is expressed by pointing
    at the releases that existed when this one was cut rather than by matching numbers.
#>
function Get-CompatibilityNote {
    param(
        [Parameter(Mandatory)][string[]]$TagPatterns
    )

    $links = @()
    foreach ($pattern in $TagPatterns) {
        $tag = (git tag -l $pattern --sort=-v:refname | Select-Object -First 1)
        if ($tag) { $links += "[$tag]($RepoUrl/releases/tag/$tag)" }
    }

    if ($links.Count -eq 0) { return '' }
    return "Compatible with: $($links -join ', ')"
}

<#
.SYNOPSIS
    Builds the release body from the notes, the compatibility line, and the asset hash.
#>
function New-ReleaseBody {
    param(
        [Parameter(Mandatory)][string]$Notes,
        [Parameter(Mandatory)][string]$AssetName,
        [Parameter(Mandatory)][string]$Sha256,
        [string]$CompatibilityNote = ''
    )

    $body = $Notes
    if ($CompatibilityNote) { $body += "`n`n$CompatibilityNote" }
    $body += "`n`n---`nSHA-256 ($AssetName): ``$Sha256``"
    return $body
}

<#
.SYNOPSIS
    Shows exactly what will be published and waits for confirmation. Returns false when
    the operator declines.
#>
function Confirm-Release {
    param(
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$AssetName,
        [Parameter(Mandatory)][string]$Sha256,
        [Parameter(Mandatory)][string]$Body,
        [Parameter(Mandatory)][double]$SizeMb,
        [switch]$Force
    )

    Write-Host ''
    Write-Host 'About to publish a GitHub Release:' -ForegroundColor Yellow
    Write-Host "  Tag / title : $Tag  (gh creates the tag at HEAD)"
    Write-Host "  Asset       : $AssetName  ($SizeMb MB)"
    Write-Host "  SHA-256     : $Sha256"
    Write-Host '  Notes       :'
    $Body -split "`n" | ForEach-Object { Write-Host "      $_" }
    Write-Host ''

    if ($Force) { return $true }

    if ((Read-Host "Type 'yes' to create and publish this release") -ne 'yes') {
        Write-Host 'Aborted — nothing was published.' -ForegroundColor Yellow
        return $false
    }

    return $true
}

<#
.SYNOPSIS
    Creates the GitHub Release, which also creates the tag at HEAD.
#>
function Publish-Release {
    param(
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$AssetPath,
        [Parameter(Mandatory)][string]$Body
    )

    $notesFile = New-TemporaryFile
    [System.IO.File]::WriteAllText($notesFile.FullName, $Body, (New-Object System.Text.UTF8Encoding($false)))

    gh release create $Tag $AssetPath --title $Title --notes-file $notesFile.FullName
    $published = ($LASTEXITCODE -eq 0)

    Remove-Item $notesFile -ErrorAction SilentlyContinue
    if (-not $published) { Fail 'gh release create failed' }

    Write-Host "Published $Tag ($(Split-Path -Leaf $AssetPath))" -ForegroundColor Green
}
