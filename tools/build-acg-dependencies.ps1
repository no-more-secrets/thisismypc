[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$workRoot = Join-Path $repoRoot 'artifacts\diagnostics\acg-dependencies'
$packageRoot = Join-Path $repoRoot 'third-party\acg\packages'
$patchRoot = Join-Path $repoRoot 'third-party\acg\patches'

$avaloniaCommit = '37fbd9655cc581ff5b1c6b1fb1be4e3118c889d0'
$skiaCommit = '9699cdf046a989423a5e5eca68e7fd813486d81c'
$avaloniaPackageHash = '85A20ABC9D520F5EFE11424720BA0524CCBE58926D5EBF48F9960C16DC04B937'
$skiaPackageHash = '8D9FF89D55D826DAF3F5205FEAC61CFECD05C52ED178860CE24324AE1652FB6E'

function Assert-LastExitCode([string]$message) {
    if ($LASTEXITCODE -ne 0) { throw $message }
}

function Get-VerifiedPackage([string]$id, [string]$version, [string]$hash) {
    $file = Join-Path $workRoot "$($id.ToLowerInvariant()).$version.nupkg"
    $url = "https://api.nuget.org/v3-flatcontainer/$($id.ToLowerInvariant())/$version/$($id.ToLowerInvariant()).$version.nupkg"
    Invoke-WebRequest -Uri $url -OutFile $file
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($actual -ne $hash) { throw "Unexpected SHA256 for $id $version. Found $actual." }
    return $file
}

function New-PatchedPackage(
    [string]$source,
    [string]$id,
    [string]$oldVersion,
    [string]$newVersion,
    [string]$assemblyRelativePath,
    [string]$assemblySource,
    [string]$expectedHash = '') {
    $unpackRoot = Join-Path $workRoot "package-$($id.ToLowerInvariant())"
    New-Item -ItemType Directory -Force $unpackRoot | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($source, $unpackRoot)
    Remove-Item -LiteralPath (Join-Path $unpackRoot '.signature.p7s') -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $assemblySource -Destination (Join-Path $unpackRoot $assemblyRelativePath) -Force

    $nuspec = Get-ChildItem $unpackRoot -Filter '*.nuspec' | Select-Object -First 1
    $core = Get-ChildItem (Join-Path $unpackRoot 'package\services\metadata\core-properties') -Filter '*.psmdcp' | Select-Object -First 1
    foreach ($metadata in @($nuspec, $core)) {
        $text = [IO.File]::ReadAllText($metadata.FullName)
        $text = $text.Replace("<version>$oldVersion</version>", "<version>$newVersion</version>")
        [IO.File]::WriteAllText($metadata.FullName, $text, [Text.UTF8Encoding]::new($false))
    }

    $target = Join-Path $packageRoot "$($id.ToLowerInvariant()).$newVersion.nupkg"
    $temporaryTarget = "$target.tmp"
    Remove-Item -LiteralPath $temporaryTarget -Force -ErrorAction SilentlyContinue
    $archive = [IO.Compression.ZipFile]::Open($temporaryTarget, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $files = Get-ChildItem -LiteralPath $unpackRoot -File -Recurse |
            Sort-Object { $_.FullName.Substring($unpackRoot.Length + 1) }
        foreach ($file in $files) {
            $relativePath = $file.FullName.Substring($unpackRoot.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($relativePath, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = $file.OpenRead()
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally { $output.Dispose(); $input.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    $actualHash = (Get-FileHash $temporaryTarget -Algorithm SHA256).Hash
    if ($expectedHash -and $actualHash -ne $expectedHash) {
        Remove-Item -LiteralPath $temporaryTarget -Force
        throw "Unexpected patched package SHA256 for $id $newVersion. Found $actualHash."
    }

    if (Test-Path -LiteralPath $target) {
        [IO.File]::Replace($temporaryTarget, $target, $null)
    }
    else {
        Move-Item -LiteralPath $temporaryTarget -Destination $target
    }
    Write-Host "$id $newVersion SHA256: $actualHash"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force $workRoot, $packageRoot | Out-Null
Get-ChildItem -LiteralPath $workRoot -Force | Remove-Item -Recurse -Force

$avaloniaSource = Join-Path $workRoot 'avalonia'
git clone --filter=blob:none https://github.com/AvaloniaUI/Avalonia.git $avaloniaSource
Assert-LastExitCode 'Avalonia clone failed.'
git -C $avaloniaSource checkout $avaloniaCommit
Assert-LastExitCode 'Avalonia checkout failed.'
git -C $avaloniaSource submodule update --init --depth 1 external/XamlX external/Numerge
Assert-LastExitCode 'Avalonia submodule checkout failed.'
Copy-Item (Join-Path $repoRoot 'global.json') (Join-Path $avaloniaSource 'global.json') -Force
git -C $avaloniaSource apply (Join-Path $patchRoot 'avalonia-win32-11.3.12.patch')
Assert-LastExitCode 'Avalonia ACG patch failed.'
dotnet restore (Join-Path $avaloniaSource 'src\Windows\Avalonia.Win32\Avalonia.Win32.csproj') `
    -p:AvsSkipBuildingLegacyTargetFrameworks=true -p:ManagePackageVersionsCentrally=false
Assert-LastExitCode 'Avalonia restore failed.'
dotnet build (Join-Path $avaloniaSource 'src\Windows\Avalonia.Win32\Avalonia.Win32.csproj') `
    --configuration Release --framework net8.0 --no-restore --maxcpucount:1 `
    -p:AvsSkipBuildingLegacyTargetFrameworks=true -p:ManagePackageVersionsCentrally=false
Assert-LastExitCode 'Avalonia build failed.'

$skiaSource = Join-Path $workRoot 'skiasharp'
git clone --filter=blob:none https://github.com/mono/SkiaSharp.git $skiaSource
Assert-LastExitCode 'SkiaSharp clone failed.'
git -C $skiaSource checkout $skiaCommit
Assert-LastExitCode 'SkiaSharp checkout failed.'
Copy-Item (Join-Path $repoRoot 'global.json') (Join-Path $skiaSource 'global.json') -Force
git -C $skiaSource apply (Join-Path $patchRoot 'skiasharp-2.88.9.patch')
Assert-LastExitCode 'SkiaSharp ACG patch failed.'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:PATH = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools;' + $env:PATH
dotnet restore (Join-Path $skiaSource 'binding\SkiaSharp\SkiaSharp.csproj') `
    -p:TargetFrameworks=net6.0 -p:ManagePackageVersionsCentrally=false
Assert-LastExitCode 'SkiaSharp restore failed.'
dotnet build (Join-Path $skiaSource 'binding\SkiaSharp\SkiaSharp.csproj') `
    --configuration Release --no-restore --maxcpucount:1 `
    -p:TargetFrameworks=net6.0 -p:ManagePackageVersionsCentrally=false
Assert-LastExitCode 'SkiaSharp build failed.'

$avaloniaPackage = Get-VerifiedPackage 'Avalonia.Win32' '11.3.12' $avaloniaPackageHash
$skiaPackage = Get-VerifiedPackage 'SkiaSharp' '2.88.9' $skiaPackageHash
New-PatchedPackage $avaloniaPackage 'Avalonia.Win32' '11.3.12' '11.3.12.2' `
    'lib\net8.0\Avalonia.Win32.dll' `
    (Join-Path $avaloniaSource 'src\Windows\Avalonia.Win32\bin\Release\net8.0\Avalonia.Win32.dll') `
    '290808B0D501D6F49F0487D0E617E0FCEE22BD866346A8A8343BD7288C339872'
New-PatchedPackage $skiaPackage 'SkiaSharp' '2.88.9' '2.88.9.2' `
    'lib\net6.0\SkiaSharp.dll' `
    (Join-Path $skiaSource 'binding\SkiaSharp\bin\Release\net6.0\SkiaSharp.dll') `
    'B2962664CBAE9761AB9EED93F5828DBD9E0A32F4E430283C08359E82AB01F4A4'
