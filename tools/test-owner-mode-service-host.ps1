# Builds and optionally runs an isolated SCM probe. It never loads restoration workers or changes consent.
[CmdletBinding()]
param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$root = Join-Path $repo 'artifacts\diagnostics\owner-service-host'
$project = Join-Path $root 'source'
$output = Join-Path $root 'publish'
New-Item -ItemType Directory -Force $project, $output | Out-Null
$sourceRoot = [Security.SecurityElement]::Escape((Join-Path $repo 'src\ThisIsMyPC.Interop.Win32'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <PublishAot>true</PublishAot>
    <ControlFlowGuard>Guard</ControlFlowGuard>
    <RestoreLockedMode>false</RestoreLockedMode>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$sourceRoot\Services\NativeServiceHost.cs" Link="NativeServiceHost.cs" />
    <Compile Include="$sourceRoot\Services\NativeServiceControl.cs" Link="NativeServiceControl.cs" />
    <Compile Include="$sourceRoot\Security\DynamicCodeHardening.cs" Link="DynamicCodeHardening.cs" />
    <Compile Include="$sourceRoot\AssemblyAttributes.cs" Link="AssemblyAttributes.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $project 'ServiceHostProbe.csproj') -Encoding utf8
@'
using ThisIsMyPC.Interop.Win32.Security;
using ThisIsMyPC.Interop.Win32.Services;

if (!DynamicCodeHardening.Apply() || !DynamicCodeHardening.IsEnabled())
    throw new InvalidOperationException("Arbitrary Code Guard is not enabled.");
using var stop = new ManualResetEventSlim();
NativeServiceHost.Run(args[0], reportRunning =>
{
    if (!DynamicCodeHardening.IsEnabled()) throw new InvalidOperationException("Guard lost during startup.");
    reportRunning();
    stop.Wait();
    if (!DynamicCodeHardening.IsEnabled()) throw new InvalidOperationException("Guard lost during shutdown.");
}, stop.Set);
'@ | Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Encoding utf8
Import-Module (Join-Path $PSScriptRoot 'ReleaseToolchain.psm1') -Force
$toolchain = Enter-PinnedReleaseToolchain
$nativeBin = Join-Path $env:VCToolsInstallDir 'bin\Hostx64\x64'
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet publish (Join-Path $project 'ServiceHostProbe.csproj') -c Release -r win-x64 `
    -p:OS=Windows_NT -p:IlcUseEnvironmentalTools=true `
    "-p:CppLinker=$nativeBin\link.exe" "-p:CppLibCreator=$nativeBin\lib.exe" `
    --artifacts-path (Join-Path $root 'build') --output $output -m:1
if ($LASTEXITCODE -ne 0) { throw 'Guarded service probe build failed.' }
if ($BuildOnly) { return }
$administrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $administrator) { throw 'Run this probe from an administrator terminal, or use -BuildOnly.' }
$name = 'TIPC-ServiceHostProbe-' + [Guid]::NewGuid().ToString('N')
$binary = Join-Path $output 'ServiceHostProbe.exe'
$binaryPath = '"' + $binary + '" ' + $name
sc.exe create $name binPath= $binaryPath start= demand | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated probe service.' }
$failure = $null
try {
    sc.exe start $name | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Guarded service startup failed.' }
    $service = Get-Service -Name $name
    $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(20))
    sc.exe interrogate $name | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Service interrogation failed.' }
    sc.exe stop $name | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Guarded service stop failed.' }
    $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
    $result = & sc.exe query $name
    if ($LASTEXITCODE -ne 0 -or ($result -join "`n") -notmatch 'WIN32_EXIT_CODE\s+: 0\s') {
        throw ('Probe service did not stop cleanly: ' + ($result -join "`n"))
    }
} catch {
    $failure = $_
    throw
} finally {
    $cleanupFailure = $null
    try {
        $service = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($service -and $service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
            sc.exe stop $name | Out-Host
            $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
        }
    } catch {
        $cleanupFailure = $_
    } finally {
        sc.exe delete $name | Out-Host
        if ($LASTEXITCODE -ne 0) { $cleanupFailure = 'Could not remove the isolated probe service.' }
    }
    if ($cleanupFailure) {
        if ($failure) { Write-Warning $cleanupFailure }
        else { throw $cleanupFailure }
    }
}
Write-Output 'Guarded NativeAOT service startup, interrogation, and shutdown passed.'
