# Read persisted review notes without treating a stopped process as deleted feedback.
[CmdletBinding()]
param([string]$Directory)

$ErrorActionPreference = 'Stop'
if (-not $Directory) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $Directory = Join-Path $repoRoot '.region-review'
    if (-not (Test-Path -LiteralPath (Join-Path $Directory 'latest.json'))) {
        $Directory = Join-Path $repoRoot 'artifacts/diagnostics/region-review'
    }
}
$recordPath = Join-Path $Directory 'latest.json'
if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) {
    [pscustomobject]@{ active = $false; reason = 'No review has been recorded.' } | ConvertTo-Json
    return
}
$selection = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
if ($selection.schemaVersion -notin @(1, 2, 3, 4)) { throw 'Unsupported region selection schema.' }
$processActive = $false
try {
    $selectionProcess = Get-Process -Id $selection.processId -ErrorAction SilentlyContinue
    $expectedStart = ([DateTimeOffset]$selection.processStartedAtUtc).UtcDateTime
    $processActive = $selectionProcess -and [Math]::Abs(($selectionProcess.StartTime.ToUniversalTime() - $expectedStart).TotalSeconds) -le 1
} catch { $processActive = $false }
$selection | Add-Member -NotePropertyName processActive -NotePropertyValue ([bool]$processActive) -Force
if ($selection.schemaVersion -eq 4) {
    $selection.active = @($selection.figures).Count -gt 0
    if (-not $processActive) { $selection.suspended = $true }
} elseif ($selection.active -and -not $processActive) {
    $selection.active = $false
    $selection | Add-Member -NotePropertyName reason -NotePropertyValue 'The legacy app session has ended.' -Force
}
if ($selection.schemaVersion -eq 4 -or $selection.active) {
    $captureDirectory = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $imagePaths = @($selection.imagePath)
    if ($selection.schemaVersion -ge 3) {
        $imagePaths += @($selection.figures | ForEach-Object { $_.imagePath })
        $imagePaths += @($selection.captures | ForEach-Object { $_.imagePath; $_.rawImagePath })
        $imagePaths += @($selection.resolvedFigures | ForEach-Object { $_.imagePath })
    }
    $missing = @()
    foreach ($recordedPath in ($imagePaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)) {
        $imagePath = [IO.Path]::GetFullPath($recordedPath)
        if (-not $imagePath.StartsWith($captureDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The selection image is outside the capture directory.'
        }
        if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) { $missing += $imagePath }
    }
    if ($selection.schemaVersion -lt 4 -and $missing.Count) { throw 'A legacy selection image is missing.' }
    $selection | Add-Member -NotePropertyName missingImages -NotePropertyValue $missing -Force
}
$selection | ConvertTo-Json -Depth 12
