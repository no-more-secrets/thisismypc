# Run after producing a DebugRelease package and a normal release installer.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DebugInstaller,
    [Parameter(Mandatory)][string]$ProductionInstaller
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $repo 'artifacts\diagnostics\debug-release-policy-test'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$guard = Join-Path $PSScriptRoot 'assert-production-signing-inputs.ps1'

# No sidecar: a copied native binary must still identify its build mode.
$debugDescription = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $DebugInstaller).Path).FileDescription
if ($debugDescription -notlike '*[[]DebugRelease unsigned[]]*') {
    throw "DebugRelease PE metadata is missing: $debugDescription"
}
if ((Get-AuthenticodeSignature -LiteralPath $DebugInstaller).Status -ne 'NotSigned') {
    throw 'Expected an unsigned DebugRelease installer.'
}
$rejected = $false
try {
    & $guard -AssetDirectory $scratch -StagingDirectory $scratch -InstallerStub $DebugInstaller
} catch {
    if ($_.Exception.Message -notlike 'DebugRelease binary cannot be signed*') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'The signing guard accepted a DebugRelease binary without its sidecar.' }
& $guard -AssetDirectory $scratch -StagingDirectory $scratch -InstallerStub $ProductionInstaller

# A caller cannot combine local trust exclusions and the signing pipeline.
$rejected = $false
try {
    & (Join-Path $PSScriptRoot 'build-release.ps1') -Version '0.0.1' -DebugRelease -SignThumbprint ('0' * 40)
} catch {
    if ($_.Exception.Message -notlike 'DebugRelease is unsigned local testing only*') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'The build accepted DebugRelease together with signing.' }
Write-Output 'DebugRelease metadata, unsigned status, copied-binary rejection, production acceptance, and signing exclusion passed.'
