[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$VpkArguments
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$toolVersion = '1.2.0'
$packageHash = 'C3A2D80CEF556DCD0DBD27B3C0363C642D79842BE6B6955DE116D30BFE2F399F'
$packageUrl = "https://api.nuget.org/v3-flatcontainer/vpk/$toolVersion/vpk.$toolVersion.nupkg"
$helperHashes = @{
    'setup.exe' = 'EEB52F602C3E442EC6D4F7CD82A3F84F64F083D0E83759194F7D98F909E24701'
    'stub.exe' = '006BC2B85917F5F277842F28DFDAC9D6AED73FDD4B7D191920C1139322A462F0'
    'update.exe' = '68C844ADC859AE56A9C1D0A503342F83755D09607D2E64D6D0FCBBF28A405628'
}
$managedAssemblyName = 'Velopack.Packaging.Windows.dll'
$managedAssemblyHash = '22DF4ACC4597CF4967A3C0882F50674ABC621B076D916011F32339BD3FC4F2FA'

function Get-Sha256([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

function Assert-Hash([string]$path, [string]$expected) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $path"
    }
    $actual = Get-Sha256 $path
    if ($actual -ne $expected) {
        throw "Unexpected SHA256 for $path. Expected $expected, found $actual."
    }
}

$manifest = Get-Content (Join-Path $repoRoot '.config\dotnet-tools.json') -Raw | ConvertFrom-Json
if ($manifest.tools.vpk.version -ne $toolVersion) {
    throw "The tool manifest must pin vpk $toolVersion."
}

$helperRoot = Join-Path $repoRoot 'third-party\velopack\bin'
foreach ($helper in $helperHashes.GetEnumerator()) {
    Assert-Hash (Join-Path $helperRoot $helper.Key) $helper.Value
}
$managedAssembly = Join-Path $repoRoot "third-party\velopack\lib\$managedAssemblyName"
Assert-Hash $managedAssembly $managedAssemblyHash

$cacheParent = Join-Path $repoRoot 'artifacts\tool-cache\velopack'
$archivePath = Join-Path $cacheParent "vpk.$toolVersion.nupkg"
$toolRoot = Join-Path $cacheParent "run-$PID"
$vpk = Join-Path $toolRoot 'tools\net10.0\any\vpk.dll'
New-Item -ItemType Directory -Force -Path $cacheParent | Out-Null
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    $downloadPath = "$archivePath.download"
    Invoke-WebRequest -UseBasicParsing -Uri $packageUrl -OutFile $downloadPath
    Assert-Hash $downloadPath $packageHash
    Move-Item -LiteralPath $downloadPath -Destination $archivePath -Force
}
Assert-Hash $archivePath $packageHash

$resolvedCache = [IO.Path]::GetFullPath($cacheParent).TrimEnd('\') + '\'
$resolvedTool = [IO.Path]::GetFullPath($toolRoot)
if (-not $resolvedTool.StartsWith($resolvedCache, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a tool directory outside $cacheParent."
}
if (Test-Path -LiteralPath $resolvedTool) {
    Remove-Item -LiteralPath $resolvedTool -Recurse -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $toolRoot)
    foreach ($helper in $helperHashes.GetEnumerator()) {
        [IO.File]::Copy(
            (Join-Path $helperRoot $helper.Key),
            (Join-Path $toolRoot "vendor\$($helper.Key)"),
            $true)
    }
    [IO.File]::Copy(
        $managedAssembly,
        (Join-Path $toolRoot "tools\net10.0\any\$managedAssemblyName"),
        $true)
    Write-Host "Using Velopack $toolVersion with hardened x64 helpers."
    & dotnet $vpk @VpkArguments
    if ($LASTEXITCODE -ne 0) { throw "vpk failed with exit code $LASTEXITCODE." }
}
finally {
    if (Test-Path -LiteralPath $resolvedTool) {
        Remove-Item -LiteralPath $resolvedTool -Recurse -Force
    }
}
