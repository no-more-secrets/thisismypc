[CmdletBinding()]
param([string]$ArchiveRoot)
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot 'release-toolchain-archive.json') -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($ArchiveRoot)) { $ArchiveRoot = $source.archiveRoot }
$ArchiveRoot = [IO.Path]::GetFullPath($ArchiveRoot).TrimEnd('\')
$inventoryPath = Join-Path $ArchiveRoot 'inventory.sha256.json'
if ((Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash -ne $source.inventorySha256) {
    throw 'The release archive inventory does not match the repository pin.'
}
$inventory = Get-Content $inventoryPath -Raw | ConvertFrom-Json
foreach ($entry in $inventory) {
    $path = [IO.Path]::GetFullPath((Join-Path $ArchiveRoot $entry.path))
    if (-not $path.StartsWith("$ArchiveRoot\", [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Archive inventory path escapes its root.'
    }
    if ((Get-Item -LiteralPath $path).Length -ne $entry.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Release archive file differs: $($entry.path)"
    }
}
Write-Host "Verified $($inventory.Count) archived release-tool files."
