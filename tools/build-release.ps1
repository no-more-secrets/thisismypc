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

    # Shown as the MSI publisher and the Program Files vendor folder. Short
    # form of the legal publisher (No More Secrets, LLC); the OV cert subject
    # carries the full name.
    [string]$Authors = 'NMS',

    # NativeAOT publish for the App (probe-proven 2026-08-31, zero trim
    # warnings; needs the VS installer dir on PATH for the native link step).
    # Default off until a full manual pass on an AOT build; the Service stays
    # CoreCLR either way.
    [switch]$Aot,

    # SHA-1 thumbprint of the SSL.com OV code-signing certificate (No More
    # Secrets, LLC) as it appears in Cert:\CurrentUser\My once the hardware
    # token is plugged in. When given, vpk runs signtool over every exe, dll,
    # and the MSI during pack, with an RFC 3161 timestamp from SSL.com, so the
    # signatures outlive the certificate. SHA256SUMS is written afterwards and
    # therefore covers the signed files. Omit for unsigned test builds.
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$SignThumbprint,

    # RFC 3161 timestamp server used with -SignThumbprint.
    [string]$TimestampUrl = 'http://ts.ssl.com'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $repoRoot "artifacts\release-staging\$Version"
$output = Join-Path $repoRoot "artifacts\releases\$Version"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'vpk not found. Install with: dotnet tool install -g vpk'
}

# Both directories are per-version scratch: vpk refuses to pack over an
# existing release of the same version, so a rebuild starts clean.
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
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

$signArgs = @()
if ($SignThumbprint) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $SignThumbprint.ToUpperInvariant() }
    if (-not $cert) { throw "No certificate with thumbprint $SignThumbprint in Cert:\CurrentUser\My. Is the token plugged in?" }
    if (-not $cert.HasPrivateKey) { throw 'Certificate found but its private key is not reachable (token driver or PIN).' }
    Write-Host "Signing as: $($cert.Subject) (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"
    $signArgs = @('--signParams', "/fd sha256 /tr $TimestampUrl /td sha256 /sha1 $SignThumbprint")
} else {
    Write-Host 'Unsigned build (no -SignThumbprint). Do not publish this.'
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
    --outputDir $output @signArgs
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

# vpk has no switch that suppresses the per-user Setup.exe, and the MSI is a
# complete per-machine install on its own (verified by msiexec /a extraction:
# it carries current\* plus Update.exe). Drop the Setup.exe and its entry in
# assets.win.json so SHA256SUMS and any vpk upload only cover shipped assets.
$setupExe = Get-ChildItem $output -Filter '*-Setup.exe'
if ($setupExe) {
    Write-Host "Removing per-user installer(s): $($setupExe.Name -join ', ')"
    $setupExe | Remove-Item -Force
}
$assetsJson = Join-Path $output 'assets.win.json'
if (Test-Path $assetsJson) {
    $assets = Get-Content $assetsJson -Raw | ConvertFrom-Json
    $kept = @($assets | Where-Object { $_.Type -ne 'Installer' })
    ConvertTo-Json $kept -Compress | Set-Content $assetsJson -Encoding utf8
}

if ($SignThumbprint) {
    Write-Host 'Verifying Authenticode signatures on the packed assets...'
    $unsigned = Get-ChildItem $output -Include *.exe, *.msi -Recurse |
        Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -ne 'Valid' }
    if ($unsigned) { throw "Unsigned or invalid signature: $($unsigned.Name -join ', ')" }
}

Write-Host 'Writing SHA256SUMS...'
& (Join-Path $PSScriptRoot 'new-release-manifest.ps1') -AssetDirectory $output

Write-Host ''
Write-Host "Release assets in $output. Next steps (docs/release/update-signing.md):"
Write-Host '  1. Sign SHA256SUMS offline: gpg --armor --detach-sign SHA256SUMS'
Write-Host "  2. Tag the release exactly v$Version"
Write-Host '  3. Upload every asset plus SHA256SUMS and SHA256SUMS.asc to that GitHub release'
