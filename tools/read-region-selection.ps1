# Reads the current development review selection. It never opens or executes files.
[CmdletBinding()]
param(
    [string]$Directory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/diagnostics/region-review')
)

$ErrorActionPreference = 'Stop'
$recordPath = Join-Path $Directory 'latest.json'
if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) {
    [pscustomobject]@{ active = $false; reason = 'No selection has been recorded.' } | ConvertTo-Json
    return
}

$selection = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
if ($selection.schemaVersion -ne 1) {
    throw 'Unsupported region selection schema.'
}

if ($selection.active) {
    $selectionProcess = Get-Process -Id $selection.processId -ErrorAction SilentlyContinue
    $expectedStart = ([DateTimeOffset]$selection.processStartedAtUtc).UtcDateTime
    if (-not $selectionProcess -or [Math]::Abs(($selectionProcess.StartTime.ToUniversalTime() - $expectedStart).TotalSeconds) -gt 1) {
        $selection.active = $false
        $selection | Add-Member -NotePropertyName reason -NotePropertyValue 'The captured app session has ended.' -Force
    } else {
        $captureDirectory = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $imagePath = [IO.Path]::GetFullPath($selection.imagePath)
        if (-not $imagePath.StartsWith($captureDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The selection image is outside the capture directory.'
        }
        if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) {
            throw 'The selection image is missing. Draw the region again.'
        }
    }
}

$selection | ConvertTo-Json -Depth 10
