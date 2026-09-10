# Downloads the pinned OpenRGB release archive into artifacts/tool-cache/openrgb/<version>/,
# verifies its SHA-256 against tools/companion-manifest.json, and extracts it next to
# the archive. The extracted OpenRGB.exe is what the App runs as the bundled lighting
# service inside a source checkout (BundledCompanionLocator), and what
# build-release.ps1 copies into the release's companions folder.
# Usage: .\tools\get-openrgb-archive.ps1   (prints the extracted executable path)
param(
    [switch]$SkipExtract
)

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$repoRoot = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $PSScriptRoot 'companion-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Companion manifest missing: $manifestPath"
}
$manifest = (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).openRgb

$cacheDirectory = Join-Path $repoRoot "artifacts\tool-cache\openrgb\$($manifest.version)"
$archivePath = Join-Path $cacheDirectory $manifest.archiveName
$expectedHash = $manifest.archiveSha256
[void](New-Item -ItemType Directory -Path $cacheDirectory -Force)

if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Cached OpenRGB archive hash is $actualHash, expected $expectedHash. Remove the invalid file: $archivePath"
    }
    Write-Host "Using verified OpenRGB cache: $archivePath"
} else {
    $temporaryPath = Join-Path $cacheDirectory ".openrgb-$([Guid]::NewGuid().ToString('N')).download"
    try {
        Write-Host "Downloading pinned $($manifest.displayName)..."
        Invoke-WebRequest -Uri $manifest.archiveUrl -OutFile $temporaryPath -UseBasicParsing
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryPath).Hash
        if ($actualHash -ne $expectedHash) {
            throw "Downloaded OpenRGB archive hash is $actualHash, expected $expectedHash. The release file changed; do not use it."
        }
        Move-Item -LiteralPath $temporaryPath -Destination $archivePath
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
    Write-Host "Cached verified OpenRGB archive: $archivePath"
}

if ($SkipExtract) {
    Write-Output $archivePath
    return
}

$extractRoot = Join-Path $cacheDirectory 'extracted'
$executable = Join-Path $extractRoot (Join-Path $manifest.archiveFolder 'OpenRGB.exe')
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    if (Test-Path -LiteralPath $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "OpenRGB.exe not found after extraction: $executable"
    }
}
Write-Host "Bundled OpenRGB executable: $executable"
Write-Output $executable
