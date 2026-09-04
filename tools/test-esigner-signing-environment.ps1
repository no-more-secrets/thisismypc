param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$CodeSignToolArchive
)

$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot 'esigner-signing-environment.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Pinned eSigner environment manifest missing: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$archivePath = (Resolve-Path -LiteralPath $CodeSignToolArchive).Path
$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
if ($archiveHash -ne $manifest.codeSignTool.archiveSha256) {
    throw "CodeSignTool archive hash is $archiveHash, expected $($manifest.codeSignTool.archiveSha256). Download version $($manifest.codeSignTool.version) from $($manifest.codeSignTool.downloadPage)"
}

$uninstallRoots = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
$install = @(
    Get-ItemProperty $uninstallRoots -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq $manifest.cka.displayName }
)
if ($install.Count -ne 1) {
    throw "Expected one installation of $($manifest.cka.displayName), found $($install.Count)."
}
if ($install[0].DisplayVersion -ne $manifest.cka.version) {
    throw "eSigner CKA version is $($install[0].DisplayVersion), expected $($manifest.cka.version)."
}
$ckaRoot = $install[0].InstallLocation
if (-not (Test-Path -LiteralPath $ckaRoot -PathType Container)) {
    throw "eSigner CKA installation directory is missing: $ckaRoot"
}

$expectedNames = @($manifest.cka.files.PSObject.Properties.Name)
foreach ($property in $manifest.cka.files.PSObject.Properties) {
    $path = Join-Path $ckaRoot $property.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Pinned eSigner CKA file is missing: $path"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actualHash -ne $property.Value) {
        throw "eSigner CKA file hash differs from the pin: $($property.Name). Found $actualHash."
    }
}

$windowsRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
foreach ($property in $manifest.cka.systemFiles.PSObject.Properties) {
    $path = Join-Path $windowsRoot $property.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Pinned eSigner KSP file is missing: $path"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actualHash -ne $property.Value) {
        throw "eSigner KSP file hash differs from the pin: $($property.Name). Found $actualHash."
    }
}

$certutil = Join-Path ([Environment]::SystemDirectory) 'certutil.exe'
$providerList = (& $certutil -v -csplist 2>&1) -join [Environment]::NewLine
$providerMatch = [regex]::Match(
    $providerList,
    '(?ms)^Provider Name:\s*eSignerKSP\s*$.*?(?=^Provider Name:|\z)')
if (-not $providerMatch.Success) {
    throw 'The pinned eSignerKSP provider is not registered with Windows CNG.'
}
$providerBlock = $providerMatch.Value
if ($providerBlock -notmatch '(?m)^\s*UM\(1\):\s*eSignerKSP\.dll\s*$') {
    throw 'The registered eSignerKSP provider does not resolve to eSignerKSP.dll.'
}
if ($providerBlock -notmatch '(?m)^\s*Impl Type:\s*1\s*\(0x1\)\s*$' -or
    $providerBlock -notmatch 'NCRYPT_IMPL_HARDWARE_FLAG') {
    throw 'The registered eSignerKSP provider is not marked as a hardware implementation.'
}

$ignoredInstallerFiles = @('unins000.exe', 'vc_redist.x86.exe')
$unexpectedRuntimeFiles = @(
    Get-ChildItem -LiteralPath $ckaRoot -File |
        Where-Object {
            $_.Extension -in '.exe', '.dll', '.config', '.tlb' -and
            $_.Name -notin $expectedNames -and
            $_.Name -notin $ignoredInstallerFiles
        }
)
if ($unexpectedRuntimeFiles.Count -gt 0) {
    throw "Unexpected executable eSigner CKA file(s): $($unexpectedRuntimeFiles.Name -join ', '). Review and pin an intentional vendor update before signing."
}

Write-Host "eSigner signing environment verified: CKA $($manifest.cka.version), CodeSignTool $($manifest.codeSignTool.version)."
