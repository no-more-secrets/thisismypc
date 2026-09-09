[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$ReleaseDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Za-z.-]+$')]
    [string]$BuildName
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$diagnosticsParent = Join-Path $repoRoot 'artifacts\diagnostics'
$diagnosticsRoot = Join-Path $diagnosticsParent "velopack-release-hardening-$BuildName"
$resolvedParent = [IO.Path]::GetFullPath($diagnosticsParent).TrimEnd('\') + '\'
$resolvedDiagnostics = [IO.Path]::GetFullPath($diagnosticsRoot)
if (-not $resolvedDiagnostics.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to replace a diagnostics directory outside artifacts.'
}
if (Test-Path -LiteralPath $resolvedDiagnostics) {
    Remove-Item -LiteralPath $resolvedDiagnostics -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedDiagnostics | Out-Null

$packages = @(Get-ChildItem -LiteralPath $releaseRoot -Filter '*-full.nupkg')
if ($packages.Count -ne 1) {
    throw "Expected one full Velopack package in $releaseRoot, found $($packages.Count)."
}
$msi = Join-Path $releaseRoot 'ThisIsMyPC-win.msi'
if (-not (Test-Path -LiteralPath $msi -PathType Leaf)) {
    throw "Velopack MSI is missing: $msi"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $stubs = @($archive.Entries | Where-Object { $_.FullName -like '*_ExecutionStub.exe' })
    if ($stubs.Count -ne 1) {
        throw "Expected one execution stub in $($packages[0].Name), found $($stubs.Count)."
    }
    $executionStub = Join-Path $resolvedDiagnostics 'ExecutionStub.exe'
    $input = $stubs[0].Open()
    $output = [IO.File]::Create($executionStub)
    try { $input.CopyTo($output) }
    finally {
        $output.Dispose()
        $input.Dispose()
    }
}
finally {
    $archive.Dispose()
}

$msiRoot = Join-Path $resolvedDiagnostics 'msi'
New-Item -ItemType Directory -Path $msiRoot | Out-Null
$extract = Start-Process msiexec.exe -ArgumentList @(
    '/a',
    ('"' + $msi + '"'),
    '/qn',
    ('TARGETDIR="' + $msiRoot + '"')
) -WindowStyle Hidden -Wait -PassThru
if ($extract.ExitCode -ne 0) {
    throw "MSI administrative extraction failed with exit code $($extract.ExitCode)."
}

$updates = @(Get-ChildItem -LiteralPath $msiRoot -Recurse -Filter Update.exe)
if ($updates.Count -ne 1) {
    throw "Expected one Update.exe in the MSI, found $($updates.Count)."
}
$installRoot = Split-Path $updates[0].FullName -Parent
$launcher = Join-Path $installRoot 'ThisIsMyPC.exe'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw "Velopack launcher is missing: $launcher"
}

& (Join-Path $PSScriptRoot 'check-binary-hardening.ps1') `
    -Path $launcher,$updates[0].FullName,$executionStub `
    -Require ThisIsMyPC.exe,Update.exe,ExecutionStub.exe
if ($LASTEXITCODE -ne 0) {
    throw 'A generated Velopack launcher is missing an exploit mitigation.'
}
