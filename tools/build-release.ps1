# Builds the machine-scope release package (docs/release/packaging.md).
# Publishes the App + the Session 0 Service into one staging directory, then
# packs a Velopack per-machine MSI (WiX 5, installs to Program Files, requires
# elevation). The per-user Setup.exe and the portable zip are deliberately not
# shipped: the app corresponds to the PC, so one elevated machine-wide install
# is the only supported shape.
# The repository tool manifest pins vpk; this script restores that exact version.
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    # Shown as the MSI publisher and the Program Files vendor folder. Short
    # form of the legal publisher (No More Secrets, LLC); the OV cert subject
    # carries the full name.
    [string]$Authors = 'NMS',

    # NativeAOT publish for the App and the Session 0 Service (both probe
    # zero trim warnings; the native link needs the VS C++ toolchain). Both
    # or neither: the two share one folder, and a CoreCLR service drags the
    # whole runtime along, which cancels any AOT saving on the app. Default
    # off until a full manual pass on an AOT build.
    [switch]$Aot,

    # SHA-1 thumbprint of the SSL.com OV code-signing certificate (No More
    # Secrets, LLC) exposed through eSigner CKA. When given, the script scans
    # and signs every first-party installed binary, the MSI, and the outer
    # installer. Canonical comparison removes each certificate table.
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$SignThumbprint,

    # eSigner certificate credential ID. Not the document eSeal ID.
    [string]$ESignerCredentialId = $env:ESIGNER_CREDENTIAL_ID,

    # Unmodified SSL.com CodeSignTool zip. The exact version and archive hash
    # are pinned in tools/esigner-signing-environment.json.
    [string]$CodeSignToolArchive = $env:ESIGNER_CODESIGNTOOL_ARCHIVE,

    # The account password is intentionally not a parameter. This build script
    # permits only a local secure prompt. CI signs in a separate short process.
    [string]$ESignerUsername = $env:ESIGNER_USERNAME,

    # This value must be identical in scan_code and SignTool /d. SSL.com binds
    # malware approval to the resulting signing digest.
    [ValidateNotNullOrEmpty()]
    [string]$SigningDescription = 'ThisIsMyPC',

    # RFC 3161 timestamp server used with -SignThumbprint.
    [string]$TimestampUrl = 'http://ts.ssl.com'
)

$ErrorActionPreference = 'Stop'
if (Test-Path Env:ESIGNER_PASSWORD) {
    throw 'Refusing to build with ESIGNER_PASSWORD in the environment. Build unsigned, then expose the secret only to sign-release-installer.ps1.'
}
$repoRoot = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $repoRoot "artifacts\staging\$Version"
$output = Join-Path $repoRoot "artifacts\releases\$Version"

$toolManifest = Join-Path $repoRoot '.config\dotnet-tools.json'
if (-not (Test-Path $toolManifest -PathType Leaf)) {
    throw "Pinned tool manifest missing: $toolManifest"
}
& (Join-Path $PSScriptRoot 'test-reproducible-build-environment.ps1')

$signerCertificate = $null
if ($SignThumbprint) {
    if ($ESignerCredentialId -notmatch '^[0-9a-fA-F-]{36}$') {
        throw 'Signing requires -ESignerCredentialId or ESIGNER_CREDENTIAL_ID.'
    }
    if ([string]::IsNullOrWhiteSpace($CodeSignToolArchive)) {
        throw 'Signing requires -CodeSignToolArchive or ESIGNER_CODESIGNTOOL_ARCHIVE.'
    }
    & (Join-Path $PSScriptRoot 'test-esigner-signing-environment.ps1') `
        -CodeSignToolArchive $CodeSignToolArchive

    $certificates = @(
        Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Thumbprint -eq $SignThumbprint.ToUpperInvariant() }
    )
    if ($certificates.Count -ne 1) {
        throw "Expected one certificate with thumbprint $SignThumbprint in Cert:\CurrentUser\My, found $($certificates.Count). Is eSigner CKA loaded?"
    }
    $signerCertificate = $certificates[0]
    if (-not $signerCertificate.HasPrivateKey) {
        throw 'Certificate found but its eSigner CKA private key is not reachable.'
    }
    $signerName = $signerCertificate.GetNameInfo(
        [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false)
    if ($signerName -ne 'No More Secrets, LLC') {
        throw "Refusing unexpected signing identity: $signerName"
    }
    $ekuExtension = $signerCertificate.Extensions |
        Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
        Select-Object -First 1
    if (-not $ekuExtension) {
        throw 'Signing certificate has no Enhanced Key Usage extension.'
    }
    $enhancedKeyUsages = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ekuExtension
    if ($enhancedKeyUsages.EnhancedKeyUsages.Value -notcontains '1.3.6.1.5.5.7.3.3') {
        throw 'Selected certificate is not authorized for code signing.'
    }
    $now = Get-Date
    if ($now -lt $signerCertificate.NotBefore -or $now -gt $signerCertificate.NotAfter) {
        throw 'Selected signing certificate is not currently valid.'
    }
}

Write-Host 'Restoring repository-pinned .NET tools...'
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Pinned .NET tool restore failed' }

# Both directories are per-version scratch: vpk refuses to pack over an
# existing release of the same version, so a rebuild starts clean.
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null
New-Item -ItemType Directory -Force $output | Out-Null

# The native link step (installer always, app with -Aot) finds the C++
# toolchain through vswhere in the VS installer directory.
$env:PATH = "$env:PATH;${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$aotArgs = @()
if ($Aot) { $aotArgs = @('-p:AotPublish=true') }

Write-Host "Publishing App ($Version, win-x64, self-contained$(if ($Aot) { ', NativeAOT' }))..."
dotnet publish (Join-Path $repoRoot 'src\ThisIsMyPC.App\ThisIsMyPC.App.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:Version=$Version @aotArgs --output $staging -m:1
if ($LASTEXITCODE -ne 0) { throw 'App publish failed' }

Write-Host "Publishing Session 0 Service into the same directory$(if ($Aot) { ' (NativeAOT)' })..."
dotnet publish (Join-Path $repoRoot 'src\ThisIsMyPC.Service\ThisIsMyPC.Service.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:Version=$Version @aotArgs --output $staging -m:1
if ($LASTEXITCODE -ne 0) { throw 'Service publish failed' }

if (-not (Test-Path (Join-Path $staging 'ThisIsMyPC.Service.exe'))) {
    throw 'ThisIsMyPC.Service.exe missing from staging; Owner Mode enable would break'
}

# Version blocks read English (United States) instead of Language Neutral
# (the compiler cannot be told otherwise). Before vpk pack, which signs them.
foreach ($exe in 'ThisIsMyPC.App.exe', 'ThisIsMyPC.Service.exe') {
    & (Join-Path $PSScriptRoot 'set-version-language.ps1') -Path (Join-Path $staging $exe)
}

# ZIP and cabinet formats copy source timestamps into their entries. Fix every
# staged timestamp so the same source and version produce the same payload.
$deterministicTimestamp = [DateTime]::SpecifyKind(
    [DateTime]'2000-01-01T00:00:00',
    [DateTimeKind]::Utc)
Get-ChildItem $staging -Recurse -Force | ForEach-Object {
    $_.CreationTimeUtc = $deterministicTimestamp
    $_.LastWriteTimeUtc = $deterministicTimestamp
    $_.LastAccessTimeUtc = $deterministicTimestamp
}
(Get-Item $staging).LastWriteTimeUtc = $deterministicTimestamp

if ($SignThumbprint) {
    Write-Host "Outer installer will be signed as: $($signerCertificate.Subject) (expires $($signerCertificate.NotAfter.ToString('yyyy-MM-dd')))"
} else {
    Write-Host 'Unsigned build (no -SignThumbprint). Do not publish this.'
}

Write-Host 'Packing per-machine MSI with Velopack...'
dotnet tool run vpk -- pack `
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

# The download users get: our own elevated installer (src/ThisIsMyPC.Installer)
# with the MSI in a length-delimited appended payload. It elevates before
# Windows Installer starts, so the UAC prompt is a normal modal, and it offers the
# options the Velopack wizard cannot (folder, shortcuts, start with Windows,
# update checks). NativeAOT always: one small native exe around the MSI.
Write-Host 'Publishing the installer (ThisIsMyPC-Installer.exe) around the MSI...'
$installerStaging = Join-Path $repoRoot "artifacts\staging\$Version-installer"
if (Test-Path $installerStaging) { Remove-Item $installerStaging -Recurse -Force }
$msiPath = Join-Path $output 'ThisIsMyPC-win.msi'
if (-not (Test-Path $msiPath)) { throw 'ThisIsMyPC-win.msi missing from the vpk output' }
& (Join-Path $PSScriptRoot 'normalize-msi.ps1') -Path $msiPath -Version $Version
dotnet publish (Join-Path $repoRoot 'src\ThisIsMyPC.Installer\ThisIsMyPC.Installer.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:Version=$Version -p:AotPublish=true -p:BundleNativeLibraries=true --output $installerStaging -m:1
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed' }
$installerExe = Join-Path $installerStaging 'ThisIsMyPC-Installer.exe'
if (-not (Test-Path $installerExe)) { throw 'ThisIsMyPC-Installer.exe missing from the installer publish output' }
# Release assets carry the version in the name (Sam, 2026-09-01).
$installerAsset = Join-Path $output "ThisIsMyPC-Installer-$Version.exe"
# The compiler stamps the version block Language Neutral; Explorer should say
# English (United States). Must precede signing: it rewrites the file.
& (Join-Path $PSScriptRoot 'set-version-language.ps1') -Path $installerExe
& (Join-Path $PSScriptRoot 'normalize-pe-timestamps.ps1') -Path $installerExe
Import-Module (Join-Path $PSScriptRoot 'InstallerBundle.psm1') -Force
Add-InstallerPayload -StubPath $installerExe -PayloadPath $msiPath -OutputPath $installerAsset | Out-Null

# Gate: every first-party binary carries ASLR, high-entropy VA, DEP, CFG,
# the /GS cookie, and table-based unwinding, read from the PE headers of the
# files about to ship (tools/check-binary-hardening.ps1 exits 1 otherwise).
Write-Host 'Checking exploit mitigations on the shipped binaries...'
& (Join-Path $PSScriptRoot 'check-binary-hardening.ps1') `
    (Join-Path $staging 'ThisIsMyPC.App.exe') (Join-Path $staging 'ThisIsMyPC.Service.exe') $installerAsset `
    -Require 'ThisIsMyPC.App.exe', 'ThisIsMyPC.Service.exe', (Split-Path $installerAsset -Leaf)
if ($LASTEXITCODE -ne 0) { throw 'A shipped binary is missing an exploit mitigation; see the table above.' }

if ($SignThumbprint) {
    & (Join-Path $PSScriptRoot 'sign-release-installer.ps1') `
        -AssetDirectory $output `
        -StagingDirectory $staging `
        -InstallerStub $installerExe `
        -Version $Version `
        -Authors $Authors `
        -SignThumbprint $SignThumbprint `
        -ESignerCredentialId $ESignerCredentialId `
        -CodeSignToolArchive $CodeSignToolArchive `
        -ESignerUsername $ESignerUsername `
        -SigningDescription $SigningDescription `
        -TimestampUrl $TimestampUrl
} else {
    Write-Host 'Writing SHA256SUMS...'
    & (Join-Path $PSScriptRoot 'new-release-manifest.ps1') -AssetDirectory $output
}

Write-Host ''
Write-Host "Release assets in $output. Next steps (docs/release/update-signing.md):"
Write-Host '  1. Sign SHA256SUMS offline: gpg --armor --detach-sign SHA256SUMS'
Write-Host "  2. Tag the release exactly v$Version"
Write-Host '  3. Upload every asset plus SHA256SUMS and SHA256SUMS.asc to that GitHub release'
