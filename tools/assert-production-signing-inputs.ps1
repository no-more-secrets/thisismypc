# Refuse local testing binaries before loading signing tools or credentials.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssetDirectory,
    [Parameter(Mandatory)][string]$StagingDirectory,
    [Parameter(Mandatory)][string]$InstallerStub
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath (Join-Path $AssetDirectory 'DEBUG-RELEASE.txt')) {
    throw 'DebugRelease packages cannot be signed as production releases.'
}
$inputs = @($InstallerStub) + @(
    Get-ChildItem -LiteralPath $AssetDirectory, $StagingDirectory -Filter '*.exe' -File -Recurse |
        Select-Object -ExpandProperty FullName
)
foreach ($inputPath in $inputs) {
    $description = [Diagnostics.FileVersionInfo]::GetVersionInfo($inputPath).FileDescription
    if ($description -like '*[[]DebugRelease unsigned[]]*') {
        throw "DebugRelease binary cannot be signed as a production release: $inputPath"
    }
}
