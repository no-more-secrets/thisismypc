# Resolve or reopen one exact persisted figure. Live apps process a queued command.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SessionId,
    [ValidateRange(1, [int]::MaxValue)][int]$FigureNumber,
    [ValidateSet('resolved', 'open')][string]$Status = 'resolved',
    [string]$ResolutionNote,
    [string]$Directory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.region-review'),
    [ValidateRange(0, 10000)][int]$WaitMilliseconds = 5000,
    [string]$FigureId
)
$ErrorActionPreference = 'Stop'
$recordPath = Join-Path $Directory 'latest.json'
if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) { throw 'No persistent review exists. Open annotations in the updated app first.' }
$lease = $null
try {
    try {
        $lease = [IO.File]::Open((Join-Path $Directory 'session.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    } catch [IO.IOException] {
        # The app owns the review. Do not edit its state behind its back.
    }
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.schemaVersion -ne 4) { throw 'Open annotations in the updated app to migrate this review first.' }
    if ($record.sessionId -ne $SessionId) { throw 'Review session changed. Read the current annotations again.' }
    $all = @($record.figures) + @($record.resolvedFigures)
    if ($FigureId) {
        $figure = @($all | Where-Object { $_.id -eq $FigureId })
    } elseif ($PSBoundParameters.ContainsKey('FigureNumber')) {
        $source = if ($Status -eq 'resolved') { @($record.figures) } else { @($record.resolvedFigures) }
        $figure = @($source | Where-Object { $_.number -eq $FigureNumber })
        if ($figure.Count -eq 0) { $figure = @($all | Where-Object { $_.number -eq $FigureNumber }) }
    } else { throw 'Specify FigureNumber or FigureId.' }
    if ($figure.Count -ne 1) { throw 'Figure was not found or its number is ambiguous. Use FigureId for archived notes.' }
    $figure = $figure[0]
    if ($null -ne $lease) {
        $already = if ($Status -eq 'resolved') { @($record.resolvedFigures | Where-Object id -eq $figure.id).Count -gt 0 } else { @($record.figures | Where-Object id -eq $figure.id).Count -gt 0 }
        if (-not $already) {
            $record.figures = @($record.figures | Where-Object id -ne $figure.id)
            $record.resolvedFigures = @($record.resolvedFigures | Where-Object id -ne $figure.id)
            $figure | Add-Member -NotePropertyName resolvedAtUtc -NotePropertyValue $(if ($Status -eq 'resolved') { [DateTime]::UtcNow.ToString('O') } else { $null }) -Force
            $figure | Add-Member -NotePropertyName resolutionNote -NotePropertyValue $(if ($Status -eq 'resolved') { $ResolutionNote } else { $null }) -Force
            if ($Status -eq 'resolved') { $record.resolvedFigures += $figure } else {
                if ($null -eq $figure.imageFigureNumber) {
                    $figure | Add-Member -NotePropertyName imageFigureNumber -NotePropertyValue $figure.number -Force
                }
                $maximum = ($record.figures | Measure-Object -Property number -Maximum).Maximum
                $figure.number = if ($record.figures.Count -eq 0) { 1 } else { [Math]::Max([int]$record.nextFigureNumber, [int]$maximum + 1) }
                $record.nextFigureNumber = $figure.number + 1
                $record.figures += $figure
            }
            if ($record.figures.Count -eq 0) { $record.nextFigureNumber = 1 }
            $record.active = $record.figures.Count -gt 0
            $record.suspended = $true
            $selected = @($record.figures | Where-Object number -eq $record.selectedFigureNumber) | Select-Object -First 1
            if ($null -eq $selected) { $selected = $record.figures | Select-Object -Last 1 }
            if ($null -ne $selected) {
                $capture = $record.captures | Where-Object id -eq $selected.captureId | Select-Object -First 1
                if ($null -eq $capture) { throw 'Figure capture metadata is missing.' }
                $record.selectionId = $selected.id
                $record.selectedFigureNumber = $selected.number
                $record.bounds = $selected.bounds
                $record.capturedAtUtc = $selected.capturedAtUtc
                $record.imagePath = $selected.imagePath
                $record.renderScale = $capture.renderScale
                $record.pixelWidth = $capture.pixelWidth
                $record.pixelHeight = $capture.pixelHeight
            } else {
                $record.selectionId = ''
                $record.selectedFigureNumber = $null
                $record.bounds = [pscustomobject]@{ x = 0; y = 0; width = 0; height = 0 }
                $record.imagePath = ''
                $record.pixelWidth = 0
                $record.pixelHeight = 0
            }
            $temp = Join-Path $Directory ('status-' + [Guid]::NewGuid().ToString('N') + '.tmp')
            $record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temp -Encoding utf8
            Move-Item -LiteralPath $temp -Destination $recordPath -Force
        }
        [pscustomobject]@{ applied = $true; status = $Status; sessionId = $SessionId; figureNumber = $figure.number; figureId = $figure.id } | ConvertTo-Json
        return
    }
    $commandDirectory = Join-Path $Directory 'commands'
    [IO.Directory]::CreateDirectory($commandDirectory) | Out-Null
    $id = [Guid]::NewGuid().ToString('N')
    $commandPath = Join-Path $commandDirectory ($id + '.json')
    $tempPath = Join-Path $commandDirectory ($id + '.tmp')
    @{ id = $id; sessionId = $SessionId; figureId = $figure.id; status = $Status; resolutionNote = $ResolutionNote } |
        ConvertTo-Json | Set-Content -LiteralPath $tempPath -Encoding utf8
    Move-Item -LiteralPath $tempPath -Destination $commandPath
    $receiptPath = Join-Path $commandDirectory ($id + '.result')
    $deadline = [DateTime]::UtcNow.AddMilliseconds($WaitMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $receiptPath)) { Start-Sleep -Milliseconds 100 }
    if (Test-Path -LiteralPath $receiptPath) { Get-Content -LiteralPath $receiptPath -Raw }
    else { [pscustomobject]@{ applied = $false; pending = $true; requestId = $id; reason = 'The app will process this when its current note edit is finished.' } | ConvertTo-Json }
} finally { if ($null -ne $lease) { $lease.Dispose() } }
