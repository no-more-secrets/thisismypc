# Rebuilds an official release from its exact tag, then compares the complete
# canonical install tree. The downloaded executable is parsed, never run.
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ReleasedInstaller
)

$ErrorActionPreference = 'Stop'
$releasedPath = (Resolve-Path -LiteralPath $ReleasedInstaller).Path
$name = Split-Path $releasedPath -Leaf
if ($name -notmatch '^ThisIsMyPC-Installer-(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\.exe$') {
    throw 'Expected a release filename such as ThisIsMyPC-Installer-1.0.0.exe.'
}
$version = $Matches.version
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('thisismypc-verify-' + [guid]::NewGuid().ToString('N'))
$cloneRoot = Join-Path $temporaryRoot 'source'
$inspectionRoot = Join-Path $temporaryRoot 'inspection'

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    New-Item -ItemType Directory -Path $inspectionRoot | Out-Null

    $signature = Get-AuthenticodeSignature -LiteralPath $releasedPath
    $signerName = if ($null -ne $signature.SignerCertificate) {
        $signature.SignerCertificate.GetNameInfo(
            [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
            $false)
    }
    if ($signature.Status -ne 'Valid' -or $signerName -cne 'No More Secrets, LLC' -or
        $null -eq $signature.TimeStamperCertificate) {
        throw 'The downloaded installer does not have a valid, timestamped No More Secrets, LLC signature.'
    }

    Write-Host "Cloning exact tag v$version into a disposable directory..."
    git clone --branch "v$version" --depth 1 `
        https://github.com/No-More-Secrets/thisismypc.git $cloneRoot
    if ($LASTEXITCODE -ne 0) { throw "Could not clone release tag v$version." }

    Push-Location $cloneRoot
    try {
        & .\tools\test-reproducible-build-environment.ps1
        Import-Module .\tools\InstallerBundle.psm1 -Force
        Import-Module .\tools\MsiPayload.psm1 -Force
        $canonicalBundle = Join-Path $inspectionRoot 'release.canonical.exe'
        $msi = Join-Path $inspectionRoot 'release.msi'
        & .\tools\normalize-authenticode-pe.ps1 `
            -Path $releasedPath -OutputPath $canonicalBundle -Quiet | Out-Null
        Export-InstallerPayload -Path $canonicalBundle -OutputPath $msi | Out-Null
        $content = Export-MsiLogicalContent `
            -Path $msi -Destination (Join-Path $inspectionRoot 'msi')
        $coreClrMarker = Join-Path $content.Payload 'current\ThisIsMyPC.App.dll'
        $isAot = -not (Test-Path -LiteralPath $coreClrMarker -PathType Leaf)
        Write-Host "Detected $(if ($isAot) { 'NativeAOT' } else { 'CoreCLR' }) release $version."

        & .\Setup.ps1
        if ($isAot) {
            & .\tools\build-release.ps1 -Version $version -Aot
        }
        else {
            & .\tools\build-release.ps1 -Version $version
        }
        $localInstaller = Join-Path $cloneRoot `
            "artifacts\releases\$version\ThisIsMyPC-Installer-$version.exe"
        & .\tools\compare-reproducible-installer.ps1 `
            -ReleasedInstaller $releasedPath `
            -LocalInstaller $localInstaller
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        $systemTemporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedTemporary.StartsWith($systemTemporary, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove an unexpected verification directory.'
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
