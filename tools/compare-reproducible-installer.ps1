# Compares a released installer with a locally rebuilt installer after removing
# the released file's Authenticode certificate table from the comparison.
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$ReleasedInstaller,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$LocalInstaller,

    [switch]$AllowUnsignedRelease
)

$ErrorActionPreference = 'Stop'
$releasedPath = (Resolve-Path $ReleasedInstaller).Path
$localPath = (Resolve-Path $LocalInstaller).Path
if (-not $AllowUnsignedRelease) {
    $signature = Get-AuthenticodeSignature -FilePath $releasedPath
    if ($signature.Status -ne 'Valid') {
        throw "Released installer Authenticode status is $($signature.Status), not Valid."
    }
}

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("thisismypc-repro-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $releasedCanonical = Join-Path $temporaryDirectory 'released.canonical.exe'
    $localCanonical = Join-Path $temporaryDirectory 'local.canonical.exe'
    & (Join-Path $PSScriptRoot 'normalize-authenticode-pe.ps1') `
        -Path $releasedPath -OutputPath $releasedCanonical
    & (Join-Path $PSScriptRoot 'normalize-authenticode-pe.ps1') `
        -Path $localPath -OutputPath $localCanonical

    $releasedHash = (Get-FileHash -LiteralPath $releasedCanonical -Algorithm SHA256).Hash
    $localHash = (Get-FileHash -LiteralPath $localCanonical -Algorithm SHA256).Hash
    if ($releasedHash -ne $localHash) {
        throw "Reproducibility check failed. Released canonical SHA256: $releasedHash. Local canonical SHA256: $localHash."
    }
    Write-Host "Reproducible installer verified: $releasedHash"
} finally {
    if (Test-Path $temporaryDirectory -PathType Container) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
