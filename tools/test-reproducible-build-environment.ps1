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

Import-Module (Join-Path $PSScriptRoot 'ReleaseToolchain.psm1') -Force
$installation = Enter-PinnedReleaseToolchain
$actualMsvc = $env:VCToolsVersion.TrimEnd('\')
$actualWindowsSdk = $env:WindowsSDKVersion.TrimEnd('\')
$linkerPath = (Get-Command link.exe -CommandType Application -ErrorAction Stop).Source
if ($actualMsvc -ne $expected.msvcToolsVersion) {
    throw "Expected MSVC tools $($expected.msvcToolsVersion); found $actualMsvc."
}
if ($actualWindowsSdk -ne $expected.windowsSdkVersion) {
    throw "Expected Windows SDK $($expected.windowsSdkVersion); found $actualWindowsSdk."
}
$signToolPath = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\$actualWindowsSdk\x64\signtool.exe"
if (-not (Test-Path $signToolPath -PathType Leaf)) { throw "signtool.exe is missing: $signToolPath" }
$actualSignToolHash = (Get-FileHash -LiteralPath $signToolPath -Algorithm SHA256).Hash
if ($actualSignToolHash -ne $expected.signToolSha256) {
    throw "Expected pinned signtool.exe SHA256 $($expected.signToolSha256); found $actualSignToolHash."
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
Write-Host "  Windows SDK $actualWindowsSdk, pinned SignTool, MsiDb.exe $actualMsiDbVersion"
