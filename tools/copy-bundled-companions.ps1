# Copies the pinned companions into a staging directory's companions folder for
# a release: companions\OpenRGB\ holds the files tools/companion-manifest.json
# lists (the headless SDK server needs no image, style or OpenGL plugins) plus a
# NOTICE naming the version, license and source. Every file is hash-checked
# against the verified archive before it is copied, so the staging tree carries
# exactly the pinned build.
# Usage: .\tools\copy-bundled-companions.ps1 -StagingDirectory <dir>
param(
    [Parameter(Mandatory = $true)]
    [string]$StagingDirectory
)

$ErrorActionPreference = 'Stop'
$manifest = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'companion-manifest.json') -Raw | ConvertFrom-Json).openRgb
$executable = & (Join-Path $PSScriptRoot 'get-openrgb-archive.ps1')
$sourceRoot = Split-Path $executable -Parent
$target = Join-Path $StagingDirectory 'companions\OpenRGB'
[void](New-Item -ItemType Directory -Path $target -Force)

foreach ($relative in $manifest.bundledFiles) {
    $source = Join-Path $sourceRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Pinned OpenRGB file missing from the verified archive: $relative"
    }
    $destination = Join-Path $target $relative
    [void](New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force)
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

$notice = @(
    "$($manifest.displayName)",
    "License: $($manifest.license). This copy is unmodified; the files here are a subset of the release archive.",
    "Source: $($manifest.sourceUrl)",
    "Archive: $($manifest.archiveUrl)",
    "Archive SHA-256: $($manifest.archiveSha256)",
    "ThisIsMyPC runs this program as a background SDK server (no window) and talks to it over the OpenRGB SDK protocol.",
    "The GPL-2.0-or-later license text is in the OpenRGB source tree linked above."
) -join "`r`n"
Set-Content -LiteralPath (Join-Path $target 'NOTICE.txt') -Value $notice -Encoding utf8

Write-Host "Bundled OpenRGB $($manifest.version): $($manifest.bundledFiles.Count) files into $target"
