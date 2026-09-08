[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$workRoot = Join-Path $repoRoot 'artifacts\diagnostics\acg-dependencies'
$packageRoot = Join-Path $repoRoot 'third-party\acg\packages'
$patchRoot = Join-Path $repoRoot 'third-party\acg\patches'

$avaloniaCommit = '37fbd9655cc581ff5b1c6b1fb1be4e3118c889d0'
$skiaCommit = '9699cdf046a989423a5e5eca68e7fd813486d81c'
$harfBuzzSkiaCommit = '64f24b1cddb68d30ec0ac6b661964178fc21d5ec'
$harfBuzzCommit = '2b3631a866b3077d9d675caa4ec9010b342b5a7c'
$sqliteVersion = '3530300'
$avaloniaPackageHash = '85A20ABC9D520F5EFE11424720BA0524CCBE58926D5EBF48F9960C16DC04B937'
$skiaPackageHash = '8D9FF89D55D826DAF3F5205FEAC61CFECD05C52ED178860CE24324AE1652FB6E'
$skiaNativePackageHash = '90FE573391A0C0719F3497DEE13DB23B934866D885EF50DDA74A5DD6F1B957FE'
$harfBuzzNativePackageHash = '526E22C0B773F57B5A0D202CB6135975EBFA74C8B2ECE0681CEAE8ACCACC632A'
$sqliteNativePackageHash = '4A22C02FFCF489792903CF263DEF9FCE27739716883F965F0B5EEFF1637430AC'
$sqliteArchiveHash = '646421E12AAC110282EF8CC68F1A62D4BB15FC7B8F09DA0B53E29EE690500431'

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

function Get-VerifiedFile([string]$url, [string]$fileName, [string]$hash) {
    $file = Join-Path $workRoot $fileName
    Invoke-WebRequest -Uri $url -OutFile $file
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($actual -ne $hash) { throw "Unexpected SHA256 for $url. Found $actual." }
    return $file
}

function Get-NativeToolchain {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'vswhere.exe is missing. Install Visual Studio C++ build tools.'
    }
    $json = & $vswhere -latest -prerelease -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json
    Assert-LastExitCode 'vswhere could not locate the C++ build tools.'
    $installations = @($json | ConvertFrom-Json)
    if ($installations.Count -ne 1) { throw 'vswhere did not return one latest C++ toolchain.' }

    $installationPath = $installations[0].installationPath
    $manifest = Get-Content (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw |
        ConvertFrom-Json
    $msvcRoot = Join-Path $installationPath "VC\Tools\MSVC\$($manifest.msvcToolsVersion)"
    return [pscustomobject]@{
        VcVars = Join-Path $installationPath 'VC\Auxiliary\Build\vcvarsall.bat'
        MSBuild = Join-Path $installationPath 'MSBuild\Current\Bin\MSBuild.exe'
        Llvm = Join-Path $installationPath 'VC\Tools\Llvm\x64'
        DumpBin = Join-Path $msvcRoot 'bin\Hostx64\x64\dumpbin.exe'
    }
}

function Assert-MatchingExports(
    [string]$package,
    [string]$packageEntry,
    [string]$replacement,
    [string]$dumpBin) {
    $compareRoot = Join-Path $workRoot 'export-comparison'
    New-Item -ItemType Directory -Force $compareRoot | Out-Null
    $original = Join-Path $compareRoot ([IO.Path]::GetFileName($packageEntry))
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry = $archive.GetEntry($packageEntry.Replace('\', '/'))
        if ($null -eq $entry) { throw "Package entry is missing: $packageEntry" }
        $input = $entry.Open()
        $output = [IO.File]::Create($original)
        try { $input.CopyTo($output) }
        finally { $output.Dispose(); $input.Dispose() }
    }
    finally { $archive.Dispose() }

    function Get-ExportEntries([string]$path) {
        $dumpOutput = @(& $dumpBin /nologo /exports $path 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "dumpbin failed while reading exports from $path."
        }

        $entries = @($dumpOutput | ForEach-Object {
            $parts = $_.Trim() -split '\s+'
            if ($parts.Count -ge 4 -and
                $parts[0] -match '^\d+$' -and
                $parts[1] -match '^[0-9A-F]+$' -and
                $parts[2] -match '^[0-9A-F]+$') {
                $nameAndForwarder = $parts[3..($parts.Count - 1)] -join ' '
                "named|$($parts[0])|$($parts[1])|$nameAndForwarder"
            }
            elseif ($parts.Count -ge 3 -and
                $parts[0] -match '^\d+$' -and
                $parts[1] -match '^[0-9A-F]+$' -and
                $parts[2] -eq '[NONAME]') {
                $nameAndForwarder = $parts[2..($parts.Count - 1)] -join ' '
                "ordinal|$($parts[0])|$nameAndForwarder"
            }
        })
        if ($entries.Count -eq 0) {
            throw "No exports found in $path."
        }
        return $entries
    }

    [string[]]$expected = Get-ExportEntries $original
    [string[]]$actual = Get-ExportEntries $replacement
    $matches = [Linq.Enumerable]::SequenceEqual(
        $expected, $actual, [StringComparer]::Ordinal)
    if (-not $matches) {
        $difference = Compare-Object $expected $actual -CaseSensitive | Select-Object -First 1
        throw "Export mismatch for $([IO.Path]::GetFileName($replacement)). " +
            "Expected $($expected.Count) entries; found $($actual.Count). First difference: $difference"
    }
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

    [IO.File]::Move($temporaryTarget, $target, $true)
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
git clone --filter=blob:none --no-checkout https://github.com/mono/SkiaSharp.git $skiaSource
Assert-LastExitCode 'SkiaSharp clone failed.'
git -C $skiaSource config core.longpaths true
git -C $skiaSource sparse-checkout init --cone
git -C $skiaSource sparse-checkout set .config binding cake externals native source
git -C $skiaSource checkout $skiaCommit
Assert-LastExitCode 'SkiaSharp checkout failed.'
Copy-Item (Join-Path $repoRoot 'global.json') (Join-Path $skiaSource 'global.json') -Force
git -C $skiaSource apply (Join-Path $patchRoot 'skiasharp-2.88.9.patch')
Assert-LastExitCode 'SkiaSharp ACG patch failed.'
git -C $skiaSource apply (Join-Path $patchRoot 'skiasharp-native-windows-2.88.9.patch')
Assert-LastExitCode 'SkiaSharp native hardening patch failed.'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:PATH = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools;' + $env:PATH
dotnet restore (Join-Path $skiaSource 'binding\SkiaSharp\SkiaSharp.csproj') `
    -p:TargetFrameworks=net6.0 -p:ManagePackageVersionsCentrally=false
Assert-LastExitCode 'SkiaSharp restore failed.'
dotnet build (Join-Path $skiaSource 'binding\SkiaSharp\SkiaSharp.csproj') `
    --configuration Release --no-restore --maxcpucount:1 `
    -p:TargetFrameworks=net6.0 -p:ManagePackageVersionsCentrally=false
Assert-LastExitCode 'SkiaSharp build failed.'

$toolchain = Get-NativeToolchain
foreach ($tool in @($toolchain.VcVars, $toolchain.MSBuild, $toolchain.Llvm, $toolchain.DumpBin)) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "Native build tool is missing: $tool" }
}

git -C $skiaSource -c core.longpaths=true submodule update --init --depth 1 `
    externals/skia externals/depot_tools
Assert-LastExitCode 'SkiaSharp native submodule checkout failed.'
$skiaNativeSource = Join-Path $skiaSource 'externals\skia'
$oldConfigCount = $env:GIT_CONFIG_COUNT
$oldConfigKey = $env:GIT_CONFIG_KEY_0
$oldConfigValue = $env:GIT_CONFIG_VALUE_0
try {
    $env:GIT_CONFIG_COUNT = '1'
    $env:GIT_CONFIG_KEY_0 = 'core.longpaths'
    $env:GIT_CONFIG_VALUE_0 = 'true'
    Push-Location $skiaNativeSource
    try {
        python tools/git-sync-deps
        Assert-LastExitCode 'Skia dependency checkout failed.'
        python bin/fetch-gn
        Assert-LastExitCode 'Skia GN download failed.'
    }
    finally { Pop-Location }
}
finally {
    $env:GIT_CONFIG_COUNT = $oldConfigCount
    $env:GIT_CONFIG_KEY_0 = $oldConfigKey
    $env:GIT_CONFIG_VALUE_0 = $oldConfigValue
}
git -C $skiaNativeSource apply (Join-Path $patchRoot 'skia-2.88.9-clang22.patch')
Assert-LastExitCode 'Skia Clang compatibility patch failed.'
dotnet tool restore --tool-manifest (Join-Path $skiaSource '.config\dotnet-tools.json')
Assert-LastExitCode 'SkiaSharp tool restore failed.'
Push-Location $skiaSource
try {
    dotnet cake native/windows/build.cake --target=libSkiaSharp `
        --configuration=Release --buildarch=x64 --supportVulkan=false `
        "--llvm=$($toolchain.Llvm)" --vcToolsetVersion=14.5
    Assert-LastExitCode 'Native SkiaSharp build failed.'
}
finally { Pop-Location }
$skiaNativeBinary = Join-Path $skiaSource 'output\native\windows\x64\libSkiaSharp.dll'

$harfBuzzSource = Join-Path $workRoot 'skiasharp-harfbuzz'
git clone --filter=blob:none --no-checkout https://github.com/mono/SkiaSharp.git $harfBuzzSource
Assert-LastExitCode 'HarfBuzzSharp source clone failed.'
git -C $harfBuzzSource config core.longpaths true
git -C $harfBuzzSource sparse-checkout init --cone
git -C $harfBuzzSource sparse-checkout set externals native
git -C $harfBuzzSource checkout $harfBuzzSkiaCommit
Assert-LastExitCode 'HarfBuzzSharp source checkout failed.'
git -C $harfBuzzSource -c core.longpaths=true submodule update --init --depth 1 externals/skia
Assert-LastExitCode 'HarfBuzzSharp Skia checkout failed.'
$harfBuzzNativeSource = Join-Path $harfBuzzSource 'externals\skia\third_party\externals\harfbuzz'
git clone --filter=blob:none --no-checkout `
    https://github.com/harfbuzz/harfbuzz.git $harfBuzzNativeSource
Assert-LastExitCode 'HarfBuzz source clone failed.'
git -C $harfBuzzNativeSource config core.longpaths true
git -C $harfBuzzNativeSource sparse-checkout init --cone
git -C $harfBuzzNativeSource sparse-checkout set src
git -C $harfBuzzNativeSource checkout $harfBuzzCommit
Assert-LastExitCode 'HarfBuzz source checkout failed.'
git -C $harfBuzzSource apply (Join-Path $patchRoot 'harfbuzzsharp-native-windows-8.3.1.patch')
Assert-LastExitCode 'HarfBuzzSharp native hardening patch failed.'
& $toolchain.MSBuild `
    (Join-Path $harfBuzzSource 'native\windows\libHarfBuzzSharp\libHarfBuzzSharp.sln') `
    /m:1 /t:Rebuild /p:Configuration=Release /p:Platform=x64 `
    /p:PlatformToolset=v145 /p:WindowsTargetPlatformVersion=10.0 /p:Deterministic=true `
    /p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false
Assert-LastExitCode 'Native HarfBuzzSharp build failed.'
$harfBuzzNativeBinary = Join-Path $harfBuzzSource `
    'native\windows\libHarfBuzzSharp\bin\x64\Release\libHarfBuzzSharp.dll'

$sqliteArchive = Get-VerifiedFile `
    "https://www.sqlite.org/2026/sqlite-amalgamation-$sqliteVersion.zip" `
    "sqlite-amalgamation-$sqliteVersion.zip" $sqliteArchiveHash
Expand-Archive -LiteralPath $sqliteArchive -DestinationPath $workRoot
$sqliteSource = Join-Path $workRoot "sqlite-amalgamation-$sqliteVersion"
Copy-Item (Join-Path $repoRoot 'third-party\acg\sqlite\sqlite-x64.rsp') $sqliteSource
Copy-Item (Join-Path $repoRoot 'third-party\acg\sqlite\sqlite-key-stubs.c') $sqliteSource
Copy-Item (Join-Path $repoRoot 'third-party\acg\sqlite\sqlite-version.rc') $sqliteSource
Remove-Item -LiteralPath `
    (Join-Path $sqliteSource 'e_sqlite3.dll'), `
    (Join-Path $sqliteSource 'e_sqlite3.exp'), `
    (Join-Path $sqliteSource 'e_sqlite3.lib') `
    -Force -ErrorAction SilentlyContinue
$sqliteBuildCommand = "`"$($toolchain.VcVars)`" amd64 >nul && " +
    "cd /d `"$sqliteSource`" && " +
    'cl.exe @sqlite-x64.rsp && ' +
    'cl.exe /nologo /c /O2 /GL /Brepro /GS /guard:cf /MT /DNDEBUG /Fosqlite-key-stubs.obj sqlite-key-stubs.c && ' +
    'rc.exe /nologo /fo sqlite-version.res sqlite-version.rc && ' +
    'link.exe /nologo /DLL /LTCG /Brepro /guard:cf /CETCOMPAT /DYNAMICBASE /NXCOMPAT ' +
    '/HIGHENTROPYVA /OUT:e_sqlite3.dll sqlite3.obj sqlite-key-stubs.obj sqlite-version.res'
& cmd.exe /d /s /c $sqliteBuildCommand
Assert-LastExitCode 'Native SQLite build failed.'
$sqliteNativeBinary = Join-Path $sqliteSource 'e_sqlite3.dll'
$sqliteFileVersion = (Get-Item -LiteralPath $sqliteNativeBinary).VersionInfo.FileVersion
if ($sqliteFileVersion -ne '3.53.3.0') {
    throw "Native SQLite file version must be 3.53.3.0. Found '$sqliteFileVersion'."
}

$avaloniaPackage = Get-VerifiedPackage 'Avalonia.Win32' '11.3.12' $avaloniaPackageHash
$skiaPackage = Get-VerifiedPackage 'SkiaSharp' '2.88.9' $skiaPackageHash
$skiaNativePackage = Get-VerifiedPackage `
    'SkiaSharp.NativeAssets.Win32' '2.88.9' $skiaNativePackageHash
$harfBuzzNativePackage = Get-VerifiedPackage `
    'HarfBuzzSharp.NativeAssets.Win32' '8.3.1.1' $harfBuzzNativePackageHash
$sqliteNativePackage = Get-VerifiedPackage `
    'SQLitePCLRaw.lib.e_sqlite3' '2.1.12' $sqliteNativePackageHash

& (Join-Path $PSScriptRoot 'check-binary-hardening.ps1') `
    -Path $skiaNativeBinary, $harfBuzzNativeBinary, $sqliteNativeBinary `
    -Require 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'e_sqlite3.dll'
Assert-LastExitCode 'Native binary hardening validation failed.'
Assert-MatchingExports $skiaNativePackage 'runtimes\win-x64\native\libSkiaSharp.dll' `
    $skiaNativeBinary $toolchain.DumpBin
Assert-MatchingExports $harfBuzzNativePackage 'runtimes\win-x64\native\libHarfBuzzSharp.dll' `
    $harfBuzzNativeBinary $toolchain.DumpBin
Assert-MatchingExports $sqliteNativePackage 'runtimes\win-x64\native\e_sqlite3.dll' `
    $sqliteNativeBinary $toolchain.DumpBin

New-PatchedPackage $avaloniaPackage 'Avalonia.Win32' '11.3.12' '11.3.12.3' `
    'lib\net8.0\Avalonia.Win32.dll' `
    (Join-Path $avaloniaSource 'src\Windows\Avalonia.Win32\bin\Release\net8.0\Avalonia.Win32.dll') `
    '20E243F26370216C677E2434539827038F096DEAED843D067FB7394C99F41244'
New-PatchedPackage $skiaPackage 'SkiaSharp' '2.88.9' '2.88.9.3' `
    'lib\net6.0\SkiaSharp.dll' `
    (Join-Path $skiaSource 'binding\SkiaSharp\bin\Release\net6.0\SkiaSharp.dll') `
    '668D0A36575F86D1BB1765E45A495308041602BA1A2EC75B70A3773ECD8037EC'
New-PatchedPackage $skiaNativePackage 'SkiaSharp.NativeAssets.Win32' '2.88.9' '2.88.9.1' `
    'runtimes\win-x64\native\libSkiaSharp.dll' $skiaNativeBinary `
    '2A1E8E0A4EDC6622B8DC82E36BBF7FC0F78EC3892AF6B411A9F262721F28C8A8'
New-PatchedPackage $harfBuzzNativePackage 'HarfBuzzSharp.NativeAssets.Win32' '8.3.1.1' '8.3.1.2' `
    'runtimes\win-x64\native\libHarfBuzzSharp.dll' $harfBuzzNativeBinary `
    'DCE2CFADB5CB7DD2F58DD927333B1B9E2AA5FFE79BA55632A4BF8B03DE1F40C6'
New-PatchedPackage $sqliteNativePackage 'SQLitePCLRaw.lib.e_sqlite3' '2.1.12' '2.1.12.3' `
    'runtimes\win-x64\native\e_sqlite3.dll' $sqliteNativeBinary `
    '6F2FABEE00A35D37118FA7E81DEB4D857C81218E249DF0A35956F404CFD5E222'
