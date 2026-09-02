# Fails when a native build tool can differ from the toolchain used for the
# official reproducible artifact. NuGet and repository tools are pinned by
# their lock files and dotnet tool manifest; this covers machine-installed SDKs.
param()

$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot 'reproducible-build-environment.json'
$expected = Get-Content $manifestPath -Raw | ConvertFrom-Json

$actualDotnet = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $actualDotnet -ne $expected.dotnetSdk) {
    throw "Expected .NET SDK $($expected.dotnetSdk); found $actualDotnet."
}

$windowsVersion = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$actualWindowsBuild = "$($windowsVersion.CurrentBuildNumber).$($windowsVersion.UBR)"
if ($actualWindowsBuild -ne $expected.windowsBuild) {
    throw "Expected Windows build $($expected.windowsBuild); found $actualWindowsBuild."
}
$windowsInstallerPath = Join-Path $env:WINDIR 'System32\msi.dll'
$actualWindowsInstaller = (Get-Item $windowsInstallerPath).VersionInfo.FileVersion
if ($actualWindowsInstaller -ne $expected.windowsInstallerFileVersion) {
    throw "Expected Windows Installer $($expected.windowsInstallerFileVersion); found $actualWindowsInstaller."
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere -PathType Leaf)) {
    throw 'vswhere.exe is missing. Install Visual Studio C++ build tools.'
}
$installationJson = & $vswhere -latest -prerelease -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json
if ($LASTEXITCODE -ne 0) { throw 'vswhere could not locate the C++ build tools.' }
$installations = @($installationJson | ConvertFrom-Json)
if ($installations.Count -ne 1) { throw 'vswhere did not return one latest C++ toolchain.' }
$installation = $installations[0]
if ($installation.installationVersion -ne $expected.visualStudioVersion) {
    throw "Expected Visual Studio $($expected.visualStudioVersion); found $($installation.installationVersion)."
}

$vcvarsall = Join-Path $installation.installationPath 'VC\Auxiliary\Build\vcvarsall.bat'
if (-not (Test-Path $vcvarsall -PathType Leaf)) { throw "vcvarsall.bat is missing: $vcvarsall" }
$probeCommand = "`"$vcvarsall`" amd64 >nul && set VCToolsVersion && set WindowsSdkVersion && where link.exe"
$probeLines = @(& cmd.exe /d /s /c $probeCommand)
if ($LASTEXITCODE -ne 0) { throw 'vcvarsall failed while probing the native toolchain.' }
$actualMsvc = ($probeLines | Where-Object { $_ -like 'VCToolsVersion=*' } | Select-Object -First 1) -replace '^VCToolsVersion=', ''
$actualWindowsSdk = (($probeLines | Where-Object { $_ -like 'WindowsSdkVersion=*' } | Select-Object -First 1) -replace '^WindowsSdkVersion=', '').TrimEnd('\')
$linkerPath = $probeLines | Where-Object { $_ -match '\\link\.exe$' } | Select-Object -First 1
if ($actualMsvc -ne $expected.msvcToolsVersion) {
    throw "Expected MSVC tools $($expected.msvcToolsVersion); found $actualMsvc."
}
if ($actualWindowsSdk -ne $expected.windowsSdkVersion) {
    throw "Expected Windows SDK $($expected.windowsSdkVersion); found $actualWindowsSdk."
}
$msiDbPath = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\$actualWindowsSdk\x86\MsiDb.exe"
if (-not (Test-Path $msiDbPath -PathType Leaf)) { throw "MsiDb.exe is missing: $msiDbPath" }
$actualMsiDbVersion = (Get-Item $msiDbPath).VersionInfo.FileVersion
$actualMsiDbHash = (Get-FileHash -LiteralPath $msiDbPath -Algorithm SHA256).Hash
if ($actualMsiDbVersion -ne $expected.msiDbFileVersion -or $actualMsiDbHash -ne $expected.msiDbSha256) {
    throw "Expected pinned MsiDb.exe $($expected.msiDbFileVersion), SHA256 $($expected.msiDbSha256); found $actualMsiDbVersion, SHA256 $actualMsiDbHash."
}
if (-not $linkerPath -or -not (Test-Path $linkerPath -PathType Leaf)) {
    throw 'vcvarsall did not expose link.exe.'
}
$actualLinker = (Get-Item $linkerPath).VersionInfo.FileVersion
if ($actualLinker -ne $expected.linkerFileVersion) {
    throw "Expected link.exe $($expected.linkerFileVersion); found $actualLinker."
}

Write-Host 'Reproducible build environment verified:'
Write-Host "  .NET SDK $actualDotnet"
Write-Host "  Windows build $actualWindowsBuild, Windows Installer $actualWindowsInstaller"
Write-Host "  Visual Studio $($installation.installationVersion)"
Write-Host "  MSVC tools $actualMsvc, link.exe $actualLinker"
Write-Host "  Windows SDK $actualWindowsSdk, MsiDb.exe $actualMsiDbVersion"
