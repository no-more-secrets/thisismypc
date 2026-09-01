# Builds the machine-scope release package (docs/release/packaging.md).
# Publishes the App + the Session 0 Service into one staging directory, then
# packs a Velopack per-machine MSI (WiX 5, installs to Program Files, requires
# elevation). The per-user Setup.exe and the portable zip are deliberately not
# shipped: the app corresponds to the PC, so one elevated machine-wide install
# is the only supported shape.
# Prerequisite: dotnet tool install -g vpk
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    # Shown as the MSI publisher and the Program Files vendor folder. Must
    # match the OV certificate subject (No More Secrets, LLC).
    [string]$Authors = 'No More Secrets, LLC',

    # NativeAOT publish for the App (probe-proven 2026-08-31, zero trim
    # warnings; needs the VS installer dir on PATH for the native link step).
    # Default off until a full manual pass on an AOT build; the Service stays
    # CoreCLR either way.
    [switch]$Aot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $repoRoot "artifacts\release-staging\$Version"
$output = Join-Path $repoRoot "artifacts\releases\$Version"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'vpk not found. Install with: dotnet tool install -g vpk'
}

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null
New-Item -ItemType Directory -Force $output | Out-Null

$aotArgs = @()
if ($Aot) {
    $aotArgs = @('-p:AotPublish=true')
    $env:PATH = "$env:PATH;${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
}

Write-Host "Publishing App ($Version, win-x64, self-contained$(if ($Aot) { ', NativeAOT' }))..."
dotnet publish (Join-Path $repoRoot 'src\ThisIsMyPC.App\ThisIsMyPC.App.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:Version=$Version @aotArgs --output $staging
if ($LASTEXITCODE -ne 0) { throw 'App publish failed' }

Write-Host 'Publishing Session 0 Service into the same directory...'
dotnet publish (Join-Path $repoRoot 'src\ThisIsMyPC.Service\ThisIsMyPC.Service.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:Version=$Version --output $staging
if ($LASTEXITCODE -ne 0) { throw 'Service publish failed' }

if (-not (Test-Path (Join-Path $staging 'ThisIsMyPC.Service.exe'))) {
    throw 'ThisIsMyPC.Service.exe missing from staging; Owner Mode enable would break'
}

Write-Host 'Packing per-machine MSI with Velopack...'
vpk pack `
    --packId ThisIsMyPC `
    --packVersion $Version `
    --packDir $staging `
    --mainExe ThisIsMyPC.App.exe `
    --packTitle ThisIsMyPC `
    --packAuthors $Authors `
    --msi --instLocation PerMachine `
    --noPortable `
    --outputDir $output
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

Write-Host 'Writing SHA256SUMS...'
& (Join-Path $PSScriptRoot 'new-release-manifest.ps1') -AssetDirectory $output

Write-Host ''
Write-Host "Release assets in $output. Next steps (docs/release/update-signing.md):"
Write-Host '  1. Sign SHA256SUMS offline: gpg --armor --detach-sign SHA256SUMS'
Write-Host "  2. Tag the release exactly v$Version"
Write-Host '  3. Upload every asset plus SHA256SUMS and SHA256SUMS.asc to that GitHub release'
