# Builds the SHA256SUMS release manifest for a directory of release assets.
# Run on release day, then sign the manifest OFFLINE with the release key:
#   gpg --armor --detach-sign SHA256SUMS
# Upload SHA256SUMS and SHA256SUMS.asc as assets on the SAME GitHub release as
# the packages; the updater fetches them from releases/download/v<version>/.
# Full procedure: docs/release/update-signing.md
param(
    [Parameter(Mandatory = $true)]
    [string]$AssetDirectory,

    [string]$OutFile
)

if (-not (Test-Path $AssetDirectory -PathType Container)) {
    throw "Asset directory not found: $AssetDirectory"
}
if (-not $OutFile) {
    $OutFile = Join-Path $AssetDirectory 'SHA256SUMS'
}

$assets = Get-ChildItem $AssetDirectory -File |
    Where-Object { $_.Name -notin @('SHA256SUMS', 'SHA256SUMS.asc') } |
    Sort-Object Name

if (-not $assets) {
    throw "No assets found in $AssetDirectory"
}

$lines = foreach ($asset in $assets) {
    $hash = (Get-FileHash $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($asset.Name)"
}

# LF endings and no BOM: the manifest must hash and sign identically everywhere.
[IO.File]::WriteAllText($OutFile, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Wrote $OutFile ($($assets.Count) assets):"
$lines | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host 'Now sign it offline with the release key:'
Write-Host "  gpg --armor --detach-sign `"$OutFile`""
