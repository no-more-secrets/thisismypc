[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$workRoot = Join-Path $repoRoot 'artifacts\diagnostics\velopack-helpers'
$sourceRoot = Join-Path $workRoot 'source'
$targetRoot = Join-Path $workRoot 'target'
$outputRoot = Join-Path $repoRoot 'third-party\velopack\bin'
$libraryRoot = Join-Path $repoRoot 'third-party\velopack\lib'
$patchPath = Join-Path $repoRoot 'third-party\velopack\patches\resource-edit-debug-directory.patch'
$sourceCommit = 'f2edcbcafb81da5b3c884aaea330e225ad91d8b6'
$rustToolchain = '1.98.1-x86_64-pc-windows-msvc'
$expectedRustCommit = '48a229ceaefd4985c50990b14116b6d856af0985'
$expectedHashes = @{
    'setup.exe' = '3EBC9447E033383E729E84895AF77D5CDEB2889B5C0F43B206CB0E22272A772D'
    'stub.exe' = '46B1F06FA14FB31091C3625D93F95A79A955AE37BC29237601F7D2291796FB69'
    'update.exe' = 'DC7BEEA1CB712D7D36624816B5D4124A89007F9473A3B9CFDF2A2B3F063D3E96'
}
$managedAssemblyName = 'Velopack.Packaging.Windows.dll'
$managedAssemblyHash = '22DF4ACC4597CF4967A3C0882F50674ABC621B076D916011F32339BD3FC4F2FA'

function Assert-LastExitCode([string]$message) {
    if ($LASTEXITCODE -ne 0) { throw $message }
}

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

$rustup = Join-Path $env:USERPROFILE '.cargo\bin\rustup.exe'
$cargo = Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe'
if (-not (Test-Path -LiteralPath $rustup -PathType Leaf) -or
    -not (Test-Path -LiteralPath $cargo -PathType Leaf)) {
    throw 'Rustup is required. Install Rustup from https://rustup.rs/.'
}

& $rustup toolchain install $rustToolchain --profile minimal
Assert-LastExitCode 'Rustup could not install the pinned Rust toolchain.'
$rustVersion = (& $rustup run $rustToolchain rustc --version --verbose) -join "`n"
Assert-LastExitCode 'Rustc version inspection failed.'
if ($rustVersion -notmatch "commit-hash: $expectedRustCommit") {
    throw "Rustc does not match pinned commit $expectedRustCommit."
}

New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot '.git') -PathType Container)) {
    & git clone https://github.com/velopack/velopack.git $sourceRoot
    Assert-LastExitCode 'Velopack clone failed.'
}
& git -C $sourceRoot fetch origin $sourceCommit
Assert-LastExitCode 'Velopack source fetch failed.'
& git -C $sourceRoot checkout --detach $sourceCommit
Assert-LastExitCode 'Velopack source checkout failed.'

$sourceChanges = @(& git -C $sourceRoot status --porcelain)
if ($sourceChanges.Count -eq 0) {
    & git -C $sourceRoot apply $patchPath
    Assert-LastExitCode 'Velopack packaging patch failed.'
}
elseif ($sourceChanges.Count -eq 3 -and
    $sourceChanges -contains ' M Cargo.lock' -and
    $sourceChanges -contains ' M Cargo.toml' -and
    $sourceChanges -contains ' M src/vpk/Velopack.Packaging.Windows/ResourceEdit.cs') {
    & git -C $sourceRoot apply --reverse --check $patchPath
    Assert-LastExitCode 'The existing Velopack source change does not match the packaging patch.'
}
else {
    throw "Velopack source has unexpected local changes: $sourceRoot"
}

$managedOutput = Join-Path $targetRoot 'managed'
$packagingProject = Join-Path $sourceRoot `
    'src\vpk\Velopack.Packaging.Windows\Velopack.Packaging.Windows.csproj'
& dotnet restore $packagingProject `
    --configfile (Join-Path $sourceRoot 'nuget.config') `
    -p:ManagePackageVersionsCentrally=false
Assert-LastExitCode 'Velopack packaging restore failed.'
& dotnet build $packagingProject --configuration Release --framework net8.0 `
    --output $managedOutput -t:Rebuild --no-restore -m:1 `
    -p:ManagePackageVersionsCentrally=false `
    -p:ContinuousIntegrationBuild=true -p:Deterministic=true `
    "-p:PathMap=$sourceRoot=/src/velopack"
Assert-LastExitCode 'Velopack packaging build failed.'

$managedAssembly = Join-Path $managedOutput $managedAssemblyName
$actualManagedHash = Get-Sha256 $managedAssembly
if ($actualManagedHash -ne $managedAssemblyHash) {
    throw "Unexpected SHA256 for $managedAssemblyName. Expected $managedAssemblyHash, found $actualManagedHash."
}
New-Item -ItemType Directory -Force -Path $libraryRoot | Out-Null
[IO.File]::Copy($managedAssembly, (Join-Path $libraryRoot $managedAssemblyName), $true)

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'vswhere.exe is missing. Install Visual Studio C++ build tools.'
}
$vsPath = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$instance = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property instanceId
if ([string]::IsNullOrWhiteSpace($vsPath) -or [string]::IsNullOrWhiteSpace($instance)) {
    throw 'Visual Studio C++ build tools are missing.'
}
Import-Module (Join-Path $vsPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell $instance -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'

$env:RUSTFLAGS = "-C target-feature=+crt-static -C control-flow-guard=yes -C link-arg=/CETCOMPAT -C link-arg=/Brepro --remap-path-prefix=$sourceRoot=/src/velopack"
$env:CARGO_TARGET_DIR = $targetRoot
& $cargo "+$rustToolchain" build --locked --release --features windows `
    -p velopack_bins --bins --manifest-path (Join-Path $sourceRoot 'Cargo.toml')
Assert-LastExitCode 'Velopack helper build failed.'

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
foreach ($helper in $expectedHashes.GetEnumerator()) {
    $source = Join-Path $targetRoot "release\$($helper.Key)"
    $target = Join-Path $outputRoot $helper.Key
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Built helper is missing: $source"
    }
    [IO.File]::Copy($source, $target, $true)
    $actual = Get-Sha256 $target
    if ($actual -ne $helper.Value) {
        throw "Unexpected SHA256 for $($helper.Key). Expected $($helper.Value), found $actual."
    }
}

& (Join-Path $PSScriptRoot 'check-binary-hardening.ps1') `
    -Path $outputRoot -Require setup.exe,stub.exe,update.exe
if ($LASTEXITCODE -ne 0) { throw 'A Velopack helper is missing an exploit mitigation.' }

Write-Host "Built reproducible Velopack helpers and packaging patch from $sourceCommit."
