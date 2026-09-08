param(
    [string]$CachePath
)

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$repoRoot = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $PSScriptRoot 'esigner-signing-environment.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Pinned eSigner environment manifest missing: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($CachePath)) {
    $archiveName = "CodeSignTool-v$($manifest.codeSignTool.version)-windows.zip"
    $CachePath = Join-Path $repoRoot "artifacts\tool-cache\esigner\$archiveName"
}
$CachePath = [IO.Path]::GetFullPath($CachePath)
$expectedHash = $manifest.codeSignTool.archiveSha256

if (Test-Path -LiteralPath $CachePath -PathType Leaf) {
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $CachePath).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Cached CodeSignTool archive hash is $actualHash, expected $expectedHash. Remove the invalid file: $CachePath"
    }
    Write-Host "Using verified CodeSignTool cache: $CachePath"
    Write-Output $CachePath
    return
}

$cacheDirectory = Split-Path $CachePath -Parent
[void](New-Item -ItemType Directory -Path $cacheDirectory -Force)
$temporaryPath = Join-Path $cacheDirectory ".codesigntool-$([Guid]::NewGuid().ToString('N')).download"
try {
    Write-Host "Downloading pinned CodeSignTool $($manifest.codeSignTool.version) from SSL.com..."
    Invoke-WebRequest -Uri $manifest.codeSignTool.archiveUrl -OutFile $temporaryPath
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryPath).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Downloaded CodeSignTool archive hash is $actualHash, expected $expectedHash. SSL.com may have changed the file."
    }
    Move-Item -LiteralPath $temporaryPath -Destination $CachePath
} finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

Write-Host "Cached verified CodeSignTool archive: $CachePath"
Write-Output $CachePath
