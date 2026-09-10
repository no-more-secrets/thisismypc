# Downloads one OpenRGB controller folder (Controllers/<Name>/) from the OpenRGB
# GitLab tree into artifacts/openrgb-src/<revision>/, together with pci_ids.h and
# the RGBController headers the ports refer to, for porting into
# src/ThisIsMyPC.Lighting (docs/lighting-controllers.md). Read-only tooling: the
# files land under artifacts/, never in the repo.
# Usage: .\tools\fetch-openrgb-controller.ps1 -Controller ENESMBusController [-Revision master]
param(
    [Parameter(Mandatory)][string]$Controller,
    [string]$Revision = 'master'
)

$ErrorActionPreference = 'Stop'
$project = 'CalcProgrammer1%2FOpenRGB'
$api = "https://gitlab.com/api/v4/projects/$project/repository"
$repoRoot = Split-Path $PSScriptRoot -Parent
$target = Join-Path $repoRoot "artifacts\openrgb-src\$Revision"
[void](New-Item -ItemType Directory -Path $target -Force)

function Get-Tree([string]$path) {
    $encoded = [Uri]::EscapeDataString($path)
    $page = 1
    $items = @()
    do {
        $uri = "$api/tree?path=$encoded&ref=$Revision&recursive=true&per_page=100&page=$page"
        $batch = Invoke-RestMethod -Uri $uri -UseBasicParsing
        $items += $batch
        $page++
    } while ($batch.Count -eq 100)
    return $items
}

function Get-RawFile([string]$path, [string]$destination) {
    $encoded = [Uri]::EscapeDataString($path)
    $uri = "$api/files/$encoded/raw?ref=$Revision"
    $directory = Split-Path -Parent $destination
    if ($directory -and -not (Test-Path -LiteralPath $directory)) { [void](New-Item -ItemType Directory -Path $directory -Force) }
    Invoke-WebRequest -Uri $uri -OutFile $destination -UseBasicParsing
}

$folder = "Controllers/$Controller"
$entries = Get-Tree $folder | Where-Object { $_.type -eq 'blob' }
if (-not $entries) { throw "No files under $folder at $Revision; check the controller folder name in OpenRGB's Controllers/ tree." }
foreach ($entry in $entries) {
    $destination = Join-Path $target ($entry.path -replace '/', '\')
    Write-Host "  $($entry.path)"
    Get-RawFile $entry.path $destination
}

foreach ($shared in 'pci_ids/pci_ids.h', 'RGBController/RGBController.h', 'RGBController/RGBController.cpp', 'RGBController/RGBControllerInterface.h', 'i2c_smbus/i2c_smbus.h', 'hidapi_wrapper/hidapi_wrapper.h') {
    $destination = Join-Path $target ($shared -replace '/', '\')
    if (-not (Test-Path -LiteralPath $destination)) {
        Write-Host "  $shared"
        Get-RawFile $shared $destination
    }
}

$commit = Invoke-RestMethod -Uri "$api/commits/$Revision" -UseBasicParsing
Write-Host "Fetched $folder at $Revision ($($commit.short_id), $($commit.committed_date)) into $target"
