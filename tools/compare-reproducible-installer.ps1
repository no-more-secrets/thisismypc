# Verifies a signed release as a canonical content tree. The launcher, MSI
# metadata, and every installed file are compared without executing the input.
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$ReleasedInstaller,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$LocalInstaller,

    [switch]$AllowUnsignedRelease,

    [string]$RequiredPublisher = 'No More Secrets, LLC'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'InstallerBundle.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'MsiPayload.psm1') -Force
$releasedPath = (Resolve-Path -LiteralPath $ReleasedInstaller).Path
$localPath = (Resolve-Path -LiteralPath $LocalInstaller).Path

function Assert-TrustedSignature([string]$path, [string]$description) {
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid') {
        throw "$description Authenticode status is $($signature.Status), not Valid: $path"
    }
    if ($null -eq $signature.SignerCertificate) {
        throw "$description has no signer certificate: $path"
    }
    $signerName = $signature.SignerCertificate.GetNameInfo(
        [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false)
    if ($signerName -cne $RequiredPublisher) {
        throw "$description has an unexpected signer: $signerName"
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "$description has no trusted timestamp: $path"
    }
}

function Get-RelativeInventory([string]$root) {
    $rootWithSeparator = $root.TrimEnd('\') + '\'
    @(
        Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
            $_.FullName.Substring($rootWithSeparator.Length).Replace('\', '/')
        } | Sort-Object
    )
}

function Get-CanonicalRecords([string]$root, [string]$temporaryRoot, [bool]$requireFirstPartySignatures) {
    $records = @()
    foreach ($relative in Get-RelativeInventory $root) {
        $path = Join-Path $root $relative.Replace('/', '\')
        $bytes = [IO.File]::ReadAllBytes($path)
        $isPe = $bytes.Length -ge 64 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A
        $leafName = Split-Path $path -Leaf
        $isFirstParty = $leafName -eq 'Update.exe' -or $leafName -match '^ThisIsMyPC(?:\..+)?\.(?:exe|dll)$'
        if ($requireFirstPartySignatures -and $isFirstParty) {
            Assert-TrustedSignature $path "Installed first-party file $relative"
        }
        if ($isPe -and $isFirstParty) {
            $canonical = Join-Path $temporaryRoot ([guid]::NewGuid().ToString('N') + '.pe')
            & (Join-Path $PSScriptRoot 'normalize-authenticode-pe.ps1') `
                -Path $path -OutputPath $canonical -Quiet | Out-Null
            $hash = (Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash
        }
        else {
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
        $records += "$relative`t$hash"
    }
    $records
}

if (-not $AllowUnsignedRelease) { Assert-TrustedSignature $releasedPath 'Released installer' }

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('thisismypc-repro-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $releasedCanonicalBundle = Join-Path $temporaryDirectory 'released.canonical.exe'
    $localCanonicalBundle = Join-Path $temporaryDirectory 'local.canonical.exe'
    & (Join-Path $PSScriptRoot 'normalize-authenticode-pe.ps1') `
        -Path $releasedPath -OutputPath $releasedCanonicalBundle -Quiet | Out-Null
    & (Join-Path $PSScriptRoot 'normalize-authenticode-pe.ps1') `
        -Path $localPath -OutputPath $localCanonicalBundle -Quiet | Out-Null

    $releasedMsi = Join-Path $temporaryDirectory 'released.msi'
    $localMsi = Join-Path $temporaryDirectory 'local.msi'
    $releasedLauncher = Join-Path $temporaryDirectory 'released.launcher.exe'
    $localLauncher = Join-Path $temporaryDirectory 'local.launcher.exe'
    Export-InstallerPayload -Path $releasedCanonicalBundle -OutputPath $releasedMsi | Out-Null
    Export-InstallerPayload -Path $localCanonicalBundle -OutputPath $localMsi | Out-Null
    Export-InstallerPayload -Path $releasedCanonicalBundle -OutputPath $releasedLauncher -LauncherOnly | Out-Null
    Export-InstallerPayload -Path $localCanonicalBundle -OutputPath $localLauncher -LauncherOnly | Out-Null

    if (-not $AllowUnsignedRelease) { Assert-TrustedSignature $releasedMsi 'Embedded MSI' }
    $releasedLauncherHash = (Get-FileHash -LiteralPath $releasedLauncher -Algorithm SHA256).Hash
    $localLauncherHash = (Get-FileHash -LiteralPath $localLauncher -Algorithm SHA256).Hash
    if ($releasedLauncherHash -ne $localLauncherHash) {
        throw "Launcher mismatch. Released canonical SHA256: $releasedLauncherHash. Local canonical SHA256: $localLauncherHash."
    }

    $releasedContent = Export-MsiLogicalContent -Path $releasedMsi -Destination (Join-Path $temporaryDirectory 'released-tree')
    $localContent = Export-MsiLogicalContent -Path $localMsi -Destination (Join-Path $temporaryDirectory 'local-tree')
    $releasedMetadataInventory = Get-RelativeInventory $releasedContent.Metadata
    $localMetadataInventory = Get-RelativeInventory $localContent.Metadata
    if ([string]::Join("`n", $releasedMetadataInventory) -cne [string]::Join("`n", $localMetadataInventory)) {
        throw 'MSI metadata inventory differs between the released and local builds.'
    }

    $rootRecords = @("launcher.exe`t$releasedLauncherHash")
    foreach ($relative in $releasedMetadataInventory) {
        $releasedFile = Join-Path $releasedContent.Metadata $relative.Replace('/', '\')
        $localFile = Join-Path $localContent.Metadata $relative.Replace('/', '\')
        $releasedHash = (Get-FileHash -LiteralPath $releasedFile -Algorithm SHA256).Hash
        $localHash = (Get-FileHash -LiteralPath $localFile -Algorithm SHA256).Hash
        if ($releasedHash -ne $localHash) {
            throw "MSI metadata mismatch at $relative. Released SHA256: $releasedHash. Local SHA256: $localHash."
        }
        $rootRecords += "metadata/$relative`t$releasedHash"
    }

    $releasedRecords = @(Get-CanonicalRecords $releasedContent.Payload $temporaryDirectory (-not $AllowUnsignedRelease))
    $localRecords = @(Get-CanonicalRecords $localContent.Payload $temporaryDirectory $false)
    if ([string]::Join("`n", $releasedRecords) -cne [string]::Join("`n", $localRecords)) {
        $releasedMap = @{}; foreach ($record in $releasedRecords) { $parts = $record -split "`t", 2; $releasedMap[$parts[0]] = $parts[1] }
        $localMap = @{}; foreach ($record in $localRecords) { $parts = $record -split "`t", 2; $localMap[$parts[0]] = $parts[1] }
        $allPaths = @($releasedMap.Keys + $localMap.Keys | Sort-Object -Unique)
        $differences = foreach ($path in $allPaths) {
            if (-not $releasedMap.ContainsKey($path)) { "local-only: $path" }
            elseif (-not $localMap.ContainsKey($path)) { "release-only: $path" }
            elseif ($releasedMap[$path] -ne $localMap[$path]) { "content mismatch: $path" }
        }
        throw "Installed payload differs:`n$($differences -join "`n")"
    }
    $rootRecords += $releasedRecords | ForEach-Object { "payload/$_" }
    $rootText = [string]::Join("`n", @($rootRecords | Sort-Object)) + "`n"
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $rootHashBytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($rootText)) } finally { $sha.Dispose() }
    $rootHash = -join ($rootHashBytes | ForEach-Object { $_.ToString('X2') })
    Write-Host "Reproducible release verified: $rootHash"
    Write-Host "  Launcher: $releasedLauncherHash"
    Write-Host "  MSI metadata files: $($releasedMetadataInventory.Count)"
    Write-Host "  Installed payload files: $($releasedRecords.Count)"
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory -PathType Container) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryDirectory)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedTemporary.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove an unexpected comparison directory.'
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
