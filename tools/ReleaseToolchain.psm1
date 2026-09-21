Set-StrictMode -Version Latest

function Select-PinnedVisualStudio {
    param([object[]]$Installations, [string]$Version)
    $matches = @($Installations | Where-Object {
        $_.installationVersion -eq $Version -and $_.isComplete -and $_.isLaunchable
    } | Sort-Object @{ Expression = { $_.productId -ne 'Microsoft.VisualStudio.Product.BuildTools' } }, installationPath)
    if ($matches.Count -eq 0) {
        throw "Visual Studio Build Tools $Version is missing. Restore the archived release toolchain; do not change the version pin."
    }
    return $matches[0]
}

function Get-PinnedVisualStudio {
    $manifest = Get-Content (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw | ConvertFrom-Json
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) { throw 'vswhere.exe is missing.' }
    $json = & $vswhere -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json
    if ($LASTEXITCODE -ne 0) { throw 'vswhere could not locate the C++ build tools.' }
    # Windows PowerShell 5.1 emits the JSON array as one pipeline object.
    # Direct assignment preserves its elements without adding an outer array.
    $installations = $json | ConvertFrom-Json
    return Select-PinnedVisualStudio -Installations $installations -Version $manifest.visualStudioVersion
}

function Enter-PinnedReleaseToolchain {
    $manifest = Get-Content (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw | ConvertFrom-Json
    $installation = Get-PinnedVisualStudio
    $devShell = Join-Path $installation.installationPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
    # A developer shell already loaded this assembly. .NET cannot load a second
    # copy with the same identity; its command can initialize the pinned instance.
    if (-not (Get-Command Enter-VsDevShell -ErrorAction SilentlyContinue)) {
        Import-Module $devShell
    }
    # A shell opened by the newer IDE can already contain its compiler paths.
    # SkipExistingEnvironmentVariables also skips PATH, so it cannot reset these.
    if ($env:__VSCMD_PREINIT_PATH) { $env:PATH = $env:__VSCMD_PREINIT_PATH }
    $devVariables = '^(VSCMD_.*|__VSCMD.*|VSINSTALLDIR|VS\d+COMNTOOLS|VCINSTALLDIR|VCTools.*|VCTargetsPath.*|VisualStudioVersion|WindowsSDK.*|WindowsLibPath|UniversalCRTSdkDir|UCRTVersion|ExtensionSdkDir|NETFXSDKDir|Framework.*|DevEnvDir|INCLUDE|EXTERNAL_INCLUDE|LIB|LIBPATH)$'
    foreach ($variable in @(Get-ChildItem Env: | Where-Object Name -match $devVariables)) {
        [Environment]::SetEnvironmentVariable($variable.Name, $null, 'Process')
    }
    Enter-VsDevShell $installation.instanceId -SkipAutomaticLocation -DevCmdArguments "-arch=x64 -host_arch=x64 -vcvars_ver=$($manifest.msvcToolsVersion) -winsdk=$($manifest.windowsSdkVersion)" | Out-Null
    $linker = Join-Path $installation.installationPath "VC\Tools\MSVC\$($manifest.msvcToolsVersion)\bin\Hostx64\x64\link.exe"
    $selectedLinker = (Get-Command link.exe -CommandType Application -ErrorAction Stop).Source
    if ($selectedLinker -ne $linker -or (Get-Item -LiteralPath $linker).VersionInfo.FileVersion -ne $manifest.linkerFileVersion) {
        throw 'The release environment did not select the pinned linker.'
    }
    if ($env:VCToolsVersion.TrimEnd('\') -ne $manifest.msvcToolsVersion -or
        $env:WindowsSDKVersion.TrimEnd('\') -ne $manifest.windowsSdkVersion) {
        throw 'The release environment did not select the pinned MSVC and Windows SDK versions.'
    }
    return $installation
}

Export-ModuleMember -Function Select-PinnedVisualStudio, Get-PinnedVisualStudio, Enter-PinnedReleaseToolchain
