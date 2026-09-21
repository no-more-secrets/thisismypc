# Build only the managed installer and open a preview that cannot install anything.
[CmdletBinding()]
param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo 'artifacts\diagnostics\installer-preview'
dotnet build (Join-Path $repo 'src\ThisIsMyPC.Installer\ThisIsMyPC.Installer.csproj') `
    --configuration Debug --artifacts-path $artifacts -p:DebugRelease=false -m:4 /nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'Installer preview build failed.' }
if (-not $BuildOnly) {
    dotnet exec (Join-Path $artifacts 'bin\ThisIsMyPC.Installer\debug\ThisIsMyPC-Installer.exe') --preview
}
