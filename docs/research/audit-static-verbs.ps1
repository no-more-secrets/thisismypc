#Requires -Version 5.1
<#
.SYNOPSIS
    Enumerates all static verb registrations across the Windows shell registry hierarchy.
    Produces a markdown report for Story 2.7 dev agent consumption.

.DESCRIPTION
    Scans the 10 registry scope paths defined in Story 2.7 AC#1.
    Writes results incrementally - each scope section is appended as it completes.
    Progress bars shown for the three large scans (extensions, ProgIDs, SystemFileAssociations).

.OUTPUTS
    Writes markdown to docs\research\static-verb-registry-audit.md

.NOTES
    Run from an elevated PowerShell prompt for full HKLM/HKCR access.
    Takes 2-5 minutes depending on registry size.
#>

[CmdletBinding()]
param(
    [string]$OutputPath
)

if (-not $OutputPath) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot }
                 elseif ($MyInvocation.MyCommand.Path) { Split-Path $MyInvocation.MyCommand.Path }
                 else { $PWD.Path }
    $OutputPath = Join-Path $scriptDir "static-verb-registry-audit.md"
}

$ErrorActionPreference = 'SilentlyContinue'

# --- Helpers ---

function Get-VerbDetails {
    [CmdletBinding()]
    param(
        [string]$VerbKeyPath,
        [string]$ScopePath,
        [string]$VerbName
    )

    $key = Get-Item -LiteralPath "Registry::$VerbKeyPath" -ErrorAction SilentlyContinue
    if (-not $key) { return $null }

    $props = @{}
    foreach ($valName in $key.GetValueNames()) {
        $props[$valName] = $key.GetValue($valName)
    }

    $default = $props['']
    if (-not $default) { $default = $props['(Default)'] }

    $commandPath = Join-Path $VerbKeyPath 'command'
    $commandKey = Get-Item -LiteralPath "Registry::$commandPath" -ErrorAction SilentlyContinue
    $commandLine = $null
    if ($commandKey) {
        $commandLine = $commandKey.GetValue('')
        if (-not $commandLine) { $commandLine = $commandKey.GetValue('(Default)') }
    }

    # Check both verb key AND command subkey for DelegateExecute
    $delegateExecute = $props['DelegateExecute']
    if (-not $delegateExecute -and $commandKey) {
        $delegateExecute = $commandKey.GetValue('DelegateExecute')
    }

    $execType = 'Unknown'
    if ($commandLine -and $delegateExecute) { $execType = 'Command+DelegateExecute' }
    elseif ($delegateExecute) { $execType = 'DelegateExecute' }
    elseif ($commandLine) { $execType = 'Command' }
    else {
        $subCommands = $props['SubCommands']
        $extSubCmdKey = $props['ExtendedSubCommandsKey']
        if ($subCommands -or $extSubCmdKey) { $execType = 'Cascading' }
    }

    $valNames = $key.GetValueNames()
    $subKeyNames = @($key.GetSubKeyNames())

    [PSCustomObject]@{
        ScopePath              = $ScopePath
        VerbName               = $VerbName
        DisplayLabel           = if ($props['MUIVerb']) { $props['MUIVerb'] } elseif ($default) { $default } else { $VerbName }
        MUIVerb                = $props['MUIVerb']
        DefaultValue           = $default
        Icon                   = $props['Icon']
        Position               = $props['Position']
        IsExtended             = $valNames -contains 'Extended'
        IsLegacyDisabled       = $valNames -contains 'LegacyDisable'
        IsProgrammaticOnly     = $valNames -contains 'ProgrammaticAccessOnly'
        HasLUAShield           = $valNames -contains 'HasLUAShield'
        NeverDefault           = $valNames -contains 'NeverDefault'
        AppliesTo              = $props['AppliesTo']
        CommandLine            = $commandLine
        DelegateExecuteClsid   = $delegateExecute
        SubCommands            = $props['SubCommands']
        ExtendedSubCommandsKey = $props['ExtendedSubCommandsKey']
        ExecType               = $execType
        SeparatorBefore        = $valNames -contains 'SeparatorBefore'
        SeparatorAfter         = $valNames -contains 'SeparatorAfter'
        SubKeyCount            = $subKeyNames.Count
        SubKeys                = ($subKeyNames -join ', ')
    }
}

function Enumerate-ShellVerbs {
    [CmdletBinding()]
    param(
        [string]$ShellKeyPath,
        [string]$Label
    )

    $key = Get-Item -LiteralPath "Registry::$ShellKeyPath" -ErrorAction SilentlyContinue
    if (-not $key) { return @() }

    $verbs = [System.Collections.Generic.List[object]]::new()
    foreach ($verbName in $key.GetSubKeyNames()) {
        $verbPath = Join-Path $ShellKeyPath $verbName
        $detail = Get-VerbDetails -VerbKeyPath $verbPath -ScopePath $Label -VerbName $verbName
        if ($detail) { $verbs.Add($detail) }
    }
    return $verbs
}

function Format-ScopeSection {
    param(
        [string]$ScopeLabel,
        [object[]]$Verbs,
        [bool]$IsCompact = $false
    )

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("### $ScopeLabel")
    [void]$sb.AppendLine("")

    if ($Verbs.Count -eq 0) {
        [void]$sb.AppendLine("*No static verbs found at this scope.*")
        [void]$sb.AppendLine("")
        return $sb.ToString()
    }

    [void]$sb.AppendLine("**$($Verbs.Count) verbs found.**")
    [void]$sb.AppendLine("")

    if ($IsCompact -and $Verbs.Count -gt 50) {
        $grouped = $Verbs | Group-Object VerbName | Sort-Object Count -Descending
        [void]$sb.AppendLine("*Large scope -- grouped by verb name (showing top 30):*")
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("| Verb | Occurrences | Exec Type | Sample Scope |")
        [void]$sb.AppendLine("|---|---|---|---|")
        $shown = 0
        foreach ($g in $grouped) {
            if ($shown -ge 30) { break }
            $sample = $g.Group[0]
            [void]$sb.AppendLine("| ``$($g.Name)`` | $($g.Count) | $($sample.ExecType) | ``$($sample.ScopePath)`` |")
            $shown++
        }
        if ($grouped.Count -gt 30) {
            [void]$sb.AppendLine("| *... and $($grouped.Count - 30) more unique verbs* | | | |")
        }
        [void]$sb.AppendLine("")

        $interesting = $Verbs | Where-Object {
            $_.DelegateExecuteClsid -or $_.IsExtended -or $_.IsLegacyDisabled -or
            $_.IsProgrammaticOnly -or $_.HasLUAShield -or $_.ExecType -eq 'Cascading'
        }
        if ($interesting.Count -gt 0) {
            [void]$sb.AppendLine("**Notable entries in this scope:**")
            [void]$sb.AppendLine("")
            foreach ($v in $interesting) {
                $flags = @()
                if ($v.IsExtended) { $flags += 'Extended' }
                if ($v.IsLegacyDisabled) { $flags += 'LegacyDisabled' }
                if ($v.IsProgrammaticOnly) { $flags += 'ProgrammaticAccessOnly' }
                if ($v.HasLUAShield) { $flags += 'HasLUAShield' }
                if ($v.DelegateExecuteClsid) { $flags += "DelegateExecute:$($v.DelegateExecuteClsid)" }
                if ($v.ExecType -eq 'Cascading') { $flags += 'Cascading' }
                [void]$sb.AppendLine("- ``$($v.ScopePath)\$($v.VerbName)`` -- $($v.DisplayLabel) [$($flags -join ', ')]")
            }
            [void]$sb.AppendLine("")
        }
    } else {
        [void]$sb.AppendLine("| Verb | Display | Exec Type | Flags | Command/CLSID |")
        [void]$sb.AppendLine("|---|---|---|---|---|")
        foreach ($v in ($Verbs | Sort-Object VerbName)) {
            $flags = @()
            if ($v.IsExtended) { $flags += 'Ext' }
            if ($v.IsLegacyDisabled) { $flags += 'Disabled' }
            if ($v.IsProgrammaticOnly) { $flags += 'ProgOnly' }
            if ($v.HasLUAShield) { $flags += 'UAC' }
            if ($v.Position) { $flags += "Pos:$($v.Position)" }
            if ($v.NeverDefault) { $flags += 'NeverDefault' }
            if ($v.AppliesTo) { $flags += 'AQS' }
            if ($v.SeparatorBefore) { $flags += 'SepBefore' }
            if ($v.SeparatorAfter) { $flags += 'SepAfter' }
            $flagStr = if ($flags) { $flags -join ', ' } else { '---' }

            $exec = switch ($v.ExecType) {
                'Command' {
                    $cmd = $v.CommandLine
                    if ($cmd -and $cmd.Length -gt 70) { $cmd = $cmd.Substring(0,67) + '...' }
                    "``$cmd``"
                }
                'DelegateExecute' { "DE: ``$($v.DelegateExecuteClsid)``" }
                'Command+DelegateExecute' { "Cmd+DE: ``$($v.DelegateExecuteClsid)``" }
                'Cascading' {
                    if ($v.SubCommands) { "Sub: $($v.SubCommands)" }
                    else { "ExtKey: ``$($v.ExtendedSubCommandsKey)``" }
                }
                default { '*unknown*' }
            }

            $display = $v.DisplayLabel
            if ($display -and $display.Length -gt 40) { $display = $display.Substring(0,37) + '...' }

            [void]$sb.AppendLine("| ``$($v.VerbName)`` | $display | $($v.ExecType) | $flagStr | $exec |")
        }
        [void]$sb.AppendLine("")
    }

    return $sb.ToString()
}

# --- Main ---

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host ""
Write-Host "  Static Verb Registry Audit" -ForegroundColor Cyan
Write-Host "  ==========================" -ForegroundColor Cyan
Write-Host ""

# Write header immediately
$header = @"
# Static Verb Registry Audit

**Date:** $(Get-Date -Format 'yyyy-MM-dd HH:mm')
**Machine:** $env:COMPUTERNAME ($([System.Environment]::OSVersion.VersionString))
**Purpose:** Ground truth for Story 2.7 (Static Verb Enumeration & Management)
**Generated by:** ``docs/research/audit-static-verbs.ps1``

---

## Detailed Results by Scope

"@

$header | Out-File -FilePath $OutputPath -Encoding UTF8
Write-Host "  Output file created: $OutputPath" -ForegroundColor DarkGray
Write-Host ""

$allVerbs = [System.Collections.Generic.List[object]]::new()

# ============================================================
# Phase 1: Fixed scope paths (fast - seconds)
# ============================================================

$fixedScopes = [ordered]@{
    'HKCR\*\shell'                    = 'HKEY_CLASSES_ROOT\*\shell'
    'HKCR\AllFilesystemObjects\shell' = 'HKEY_CLASSES_ROOT\AllFilesystemObjects\shell'
    'HKCR\Directory\shell'            = 'HKEY_CLASSES_ROOT\Directory\shell'
    'HKCR\Folder\shell'               = 'HKEY_CLASSES_ROOT\Folder\shell'
    'HKCR\Directory\Background\shell' = 'HKEY_CLASSES_ROOT\Directory\Background\shell'
    'HKCR\DesktopBackground\shell'    = 'HKEY_CLASSES_ROOT\DesktopBackground\shell'
    'HKCR\Drive\shell'                = 'HKEY_CLASSES_ROOT\Drive\shell'
}

Write-Host "  Phase 1/4: Fixed scope paths" -ForegroundColor Yellow
foreach ($label in $fixedScopes.Keys) {
    $regPath = $fixedScopes[$label]
    $verbs = Enumerate-ShellVerbs -ShellKeyPath $regPath -Label $label
    $allVerbs.AddRange($verbs)

    $section = Format-ScopeSection -ScopeLabel $label -Verbs $verbs
    $section | Out-File -FilePath $OutputPath -Encoding UTF8 -Append

    Write-Host "    $label - $($verbs.Count) verbs" -ForegroundColor Green
}
Write-Host ""

# ============================================================
# Phase 2: SystemFileAssociations (medium - 10-30s)
# ============================================================

Write-Host "  Phase 2/4: SystemFileAssociations" -ForegroundColor Yellow
$sfaRoot = Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\SystemFileAssociations' -ErrorAction SilentlyContinue
$sfaVerbs = [System.Collections.Generic.List[object]]::new()

if ($sfaRoot) {
    $sfaKeys = @($sfaRoot.GetSubKeyNames())
    $sfaTotal = $sfaKeys.Count
    $sfaIndex = 0

    foreach ($kind in $sfaKeys) {
        $sfaIndex++
        $pct = [math]::Floor(($sfaIndex / $sfaTotal) * 100)
        Write-Progress -Activity "Scanning SystemFileAssociations" -Status "$kind ($sfaIndex / $sfaTotal)" -PercentComplete $pct -Id 1

        $shellPath = "HKEY_CLASSES_ROOT\SystemFileAssociations\$kind\shell"
        $verbs = Enumerate-ShellVerbs -ShellKeyPath $shellPath -Label "HKCR\SystemFileAssociations\$kind\shell"
        if ($verbs.Count -gt 0) {
            $sfaVerbs.AddRange($verbs)
        }
    }
    Write-Progress -Activity "Scanning SystemFileAssociations" -Completed -Id 1
}

$allVerbs.AddRange($sfaVerbs)
$section = Format-ScopeSection -ScopeLabel 'HKCR\SystemFileAssociations\<type>\shell' -Verbs $sfaVerbs -IsCompact $true
$section | Out-File -FilePath $OutputPath -Encoding UTF8 -Append
Write-Host "    $($sfaVerbs.Count) verbs across $($sfaKeys.Count) types" -ForegroundColor Green
Write-Host ""

# ============================================================
# Phase 3: .ext\shell (medium-large - 30-90s)
# ============================================================

Write-Host "  Phase 3/4: File extensions (.ext\shell)" -ForegroundColor Yellow
$extKeys = @(Get-ChildItem 'Registry::HKEY_CLASSES_ROOT' -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -match '^\.' })
$extTotal = $extKeys.Count
$extIndex = 0
$extVerbs = [System.Collections.Generic.List[object]]::new()
$extWithVerbs = 0

foreach ($ext in $extKeys) {
    $extIndex++
    if ($extIndex % 50 -eq 0 -or $extIndex -eq $extTotal) {
        $pct = [math]::Floor(($extIndex / $extTotal) * 100)
        Write-Progress -Activity "Scanning file extensions" -Status "$($ext.PSChildName) ($extIndex / $extTotal)" -PercentComplete $pct -Id 2
    }

    $shellPath = "HKEY_CLASSES_ROOT\$($ext.PSChildName)\shell"
    $verbs = Enumerate-ShellVerbs -ShellKeyPath $shellPath -Label "HKCR\$($ext.PSChildName)\shell"
    if ($verbs.Count -gt 0) {
        $extVerbs.AddRange($verbs)
        $extWithVerbs++
    }
}
Write-Progress -Activity "Scanning file extensions" -Completed -Id 2

$allVerbs.AddRange($extVerbs)
$section = Format-ScopeSection -ScopeLabel 'HKCR\.ext\shell' -Verbs $extVerbs -IsCompact $true
$section | Out-File -FilePath $OutputPath -Encoding UTF8 -Append
Write-Host "    $($extVerbs.Count) verbs across $extWithVerbs of $extTotal extensions" -ForegroundColor Green
Write-Host ""

# ============================================================
# Phase 4: ProgID\shell (largest - 1-3 min)
# ============================================================

Write-Host "  Phase 4/4: ProgIDs (largest scan)" -ForegroundColor Yellow

# Pre-filter: skip known non-ProgID keys and keys already scanned
$skipNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($s in @('*','AllFilesystemObjects','Directory','Folder','Drive',
                  'DesktopBackground','CLSID','Interface','TypeLib',
                  'SystemFileAssociations','Installer','MIME','Protocols',
                  'Record','SafeMode','AppID','Applications','ActivatableClasses',
                  'DirectShow','MediaFoundation','WOW6432Node','Component Categories')) {
    [void]$skipNames.Add($s)
}

$progIdKeys = @(Get-ChildItem 'Registry::HKEY_CLASSES_ROOT' -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -notmatch '^\.' -and -not $skipNames.Contains($_.PSChildName) })
$pidTotal = $progIdKeys.Count
$pidIndex = 0
$progIdVerbs = [System.Collections.Generic.List[object]]::new()
$pidWithVerbs = 0

foreach ($pid in $progIdKeys) {
    $pidIndex++
    if ($pidIndex % 100 -eq 0 -or $pidIndex -eq $pidTotal) {
        $pct = [math]::Floor(($pidIndex / $pidTotal) * 100)
        Write-Progress -Activity "Scanning ProgIDs" -Status "$($pid.PSChildName) ($pidIndex / $pidTotal)" -PercentComplete $pct -Id 3
    }

    # Quick check: does this key even have a \shell subkey?
    $shellPath = "HKEY_CLASSES_ROOT\$($pid.PSChildName)\shell"
    $shellKey = Get-Item -LiteralPath "Registry::$shellPath" -ErrorAction SilentlyContinue
    if ($shellKey -and $shellKey.SubKeyCount -gt 0) {
        $verbs = Enumerate-ShellVerbs -ShellKeyPath $shellPath -Label "HKCR\$($pid.PSChildName)\shell"
        if ($verbs.Count -gt 0) {
            $progIdVerbs.AddRange($verbs)
            $pidWithVerbs++
        }
    }
}
Write-Progress -Activity "Scanning ProgIDs" -Completed -Id 3

$allVerbs.AddRange($progIdVerbs)
$section = Format-ScopeSection -ScopeLabel 'HKCR\<ProgID>\shell' -Verbs $progIdVerbs -IsCompact $true
$section | Out-File -FilePath $OutputPath -Encoding UTF8 -Append
Write-Host "    $($progIdVerbs.Count) verbs across $pidWithVerbs of $pidTotal ProgIDs" -ForegroundColor Green
Write-Host ""

# ============================================================
# Compute statistics and write summary + analysis sections
# ============================================================

$stopwatch.Stop()
$elapsed = $stopwatch.Elapsed

$delegateCount = ($allVerbs | Where-Object { $_.DelegateExecuteClsid }).Count
$commandCount  = ($allVerbs | Where-Object { $_.ExecType -eq 'Command' }).Count
$cascadingCount = ($allVerbs | Where-Object { $_.ExecType -eq 'Cascading' }).Count
$bothCount     = ($allVerbs | Where-Object { $_.ExecType -eq 'Command+DelegateExecute' }).Count
$unknownCount  = ($allVerbs | Where-Object { $_.ExecType -eq 'Unknown' }).Count
$disabledCount = ($allVerbs | Where-Object { $_.IsLegacyDisabled }).Count
$extendedCount = ($allVerbs | Where-Object { $_.IsExtended }).Count
$progOnlyCount = ($allVerbs | Where-Object { $_.IsProgrammaticOnly }).Count
$luaCount      = ($allVerbs | Where-Object { $_.HasLUAShield }).Count

Write-Host "  ==========================" -ForegroundColor Cyan
Write-Host "  COMPLETE in $([math]::Round($elapsed.TotalSeconds))s" -ForegroundColor Cyan
Write-Host "  Total verbs: $($allVerbs.Count)" -ForegroundColor Yellow
Write-Host "    Command:          $commandCount"
Write-Host "    DelegateExecute:  $delegateCount"
Write-Host "    Both (Cmd+DE):    $bothCount"
Write-Host "    Cascading:        $cascadingCount"
Write-Host "    Unknown:          $unknownCount"
Write-Host "    LegacyDisabled:   $disabledCount"
Write-Host "    Extended:         $extendedCount"
Write-Host "    ProgOnly:         $progOnlyCount"
Write-Host "    HasLUAShield:     $luaCount"
Write-Host ""

# Build the remaining sections (summary, key findings, surprises)
$tail = [System.Text.StringBuilder]::new()

[void]$tail.AppendLine("")
[void]$tail.AppendLine("---")
[void]$tail.AppendLine("")
[void]$tail.AppendLine("## Summary")
[void]$tail.AppendLine("")
[void]$tail.AppendLine("*Scan completed in $([math]::Round($elapsed.TotalSeconds)) seconds.*")
[void]$tail.AppendLine("")
[void]$tail.AppendLine("| Metric | Count |")
[void]$tail.AppendLine("|---|---|")
[void]$tail.AppendLine("| Total static verbs | $($allVerbs.Count) |")
[void]$tail.AppendLine("| Command verbs (``command\(Default)``) | $commandCount |")
[void]$tail.AppendLine("| DelegateExecute verbs (COM-delegated) | $delegateCount |")
[void]$tail.AppendLine("| Both Command + DelegateExecute | $bothCount |")
[void]$tail.AppendLine("| Cascading (SubCommands/ExtendedSubCommandsKey) | $cascadingCount |")
[void]$tail.AppendLine("| Unknown execution type | $unknownCount |")
[void]$tail.AppendLine("| LegacyDisabled | $disabledCount |")
[void]$tail.AppendLine("| Extended (Shift-only) | $extendedCount |")
[void]$tail.AppendLine("| ProgrammaticAccessOnly | $progOnlyCount |")
[void]$tail.AppendLine("| HasLUAShield (UAC) | $luaCount |")
[void]$tail.AppendLine("")

# Key findings
[void]$tail.AppendLine("## Key Findings for Dev Agent")
[void]$tail.AppendLine("")

if ($delegateCount -gt 0) {
    [void]$tail.AppendLine("### DelegateExecute Verbs Found")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("These verbs use COM delegation instead of a command line. ``LegacyDisable`` still works on them.")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("| Scope | Verb | Display | CLSID |")
    [void]$tail.AppendLine("|---|---|---|---|")
    foreach ($v in ($allVerbs | Where-Object { $_.DelegateExecuteClsid })) {
        [void]$tail.AppendLine("| ``$($v.ScopePath)`` | ``$($v.VerbName)`` | $($v.DisplayLabel) | ``$($v.DelegateExecuteClsid)`` |")
    }
    [void]$tail.AppendLine("")
} else {
    [void]$tail.AppendLine("### No DelegateExecute Verbs Found")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("All verbs use standard ``command\(Default)`` execution. The scanner can treat DelegateExecute as an edge case.")
    [void]$tail.AppendLine("")
}

if ($cascadingCount -gt 0) {
    [void]$tail.AppendLine("### Cascading Verbs (Submenus)")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("These verbs point to submenus via ``SubCommands`` or ``ExtendedSubCommandsKey``. For Beta, read top-level only.")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("| Scope | Verb | Display | SubCommands | ExtendedSubCommandsKey |")
    [void]$tail.AppendLine("|---|---|---|---|---|")
    foreach ($v in ($allVerbs | Where-Object { $_.ExecType -eq 'Cascading' -or $_.SubCommands -or $_.ExtendedSubCommandsKey })) {
        [void]$tail.AppendLine("| ``$($v.ScopePath)`` | ``$($v.VerbName)`` | $($v.DisplayLabel) | $($v.SubCommands) | $($v.ExtendedSubCommandsKey) |")
    }
    [void]$tail.AppendLine("")
}

if ($bothCount -gt 0) {
    [void]$tail.AppendLine("### Verbs with Both Command AND DelegateExecute")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("These have a fallback command line plus COM delegation. Shell uses DelegateExecute if available.")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("| Scope | Verb | Display | Command | CLSID |")
    [void]$tail.AppendLine("|---|---|---|---|---|")
    foreach ($v in ($allVerbs | Where-Object { $_.ExecType -eq 'Command+DelegateExecute' })) {
        $cmd = $v.CommandLine
        if ($cmd -and $cmd.Length -gt 60) { $cmd = $cmd.Substring(0,57) + '...' }
        [void]$tail.AppendLine("| ``$($v.ScopePath)`` | ``$($v.VerbName)`` | $($v.DisplayLabel) | ``$cmd`` | ``$($v.DelegateExecuteClsid)`` |")
    }
    [void]$tail.AppendLine("")
}

# Canonical verbs
$canonicalNames = @('open','opennew','print','printto','explore','find','properties','openas','edit','play','preview','runas')
$canonicalFound = $allVerbs | Where-Object { $_.VerbName -in $canonicalNames }
if ($canonicalFound.Count -gt 0) {
    [void]$tail.AppendLine("### Canonical Verbs Detected")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("These have special Shell behavior. Classify as ``Critical`` -- warn before disable.")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("| Scope | Verb | Display | Exec Type |")
    [void]$tail.AppendLine("|---|---|---|---|")
    foreach ($v in $canonicalFound) {
        [void]$tail.AppendLine("| ``$($v.ScopePath)`` | ``$($v.VerbName)`` | $($v.DisplayLabel) | $($v.ExecType) |")
    }
    [void]$tail.AppendLine("")
}

# Cross-reference vs SUMMARY.md
[void]$tail.AppendLine("### Cross-Reference vs SUMMARY.md Predictions")
[void]$tail.AppendLine("")

$vsCode = $allVerbs | Where-Object { $_.VerbName -like '*AnyCode*' -or $_.VerbName -like '*Code*' -or $_.DisplayLabel -like '*Visual Studio*' }
$wizTree = $allVerbs | Where-Object { $_.VerbName -like '*WizTree*' -or $_.DisplayLabel -like '*WizTree*' }
$terminal = $allVerbs | Where-Object { $_.VerbName -like '*Terminal*' -or $_.DisplayLabel -like '*Terminal*' -or $_.VerbName -like '*wt*' }

[void]$tail.AppendLine("| Prediction | Expected | Actual |")
[void]$tail.AppendLine("|---|---|---|")
[void]$tail.AppendLine("| Visual Studio at ``Directory\Background\shell\AnyCode`` | Yes | $(if ($vsCode) { 'Found: ' + (($vsCode | ForEach-Object { $_.ScopePath + '\' + $_.VerbName }) -join ', ') } else { 'NOT FOUND' }) |")
[void]$tail.AppendLine("| WizTree at ``Directory\shell`` AND ``Directory\Background\shell`` | Dual registration | $(if ($wizTree) { 'Found: ' + (($wizTree | ForEach-Object { $_.ScopePath + '\' + $_.VerbName }) -join ', ') } else { 'NOT FOUND (may not be installed)' }) |")
[void]$tail.AppendLine("| Windows Terminal as PackagedCom only (NO static verb) | No ``shell\`` entry | $(if ($terminal) { 'FOUND (unexpected): ' + (($terminal | ForEach-Object { $_.ScopePath + '\' + $_.VerbName }) -join ', ') } else { 'Confirmed: no static verb entry' }) |")
[void]$tail.AppendLine("")

# Surprises
[void]$tail.AppendLine("---")
[void]$tail.AppendLine("")
[void]$tail.AppendLine("## Surprises & Anomalies")
[void]$tail.AppendLine("")
[void]$tail.AppendLine("*Auto-detected patterns. Add manual observations below.*")
[void]$tail.AppendLine("")

$unknowns = $allVerbs | Where-Object { $_.ExecType -eq 'Unknown' }
if ($unknowns.Count -gt 0) {
    [void]$tail.AppendLine("### Verbs with No Command, DelegateExecute, or SubCommands")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("These verb keys exist but have no known execution mechanism.")
    [void]$tail.AppendLine("")
    foreach ($v in $unknowns) {
        [void]$tail.AppendLine("- ``$($v.ScopePath)\$($v.VerbName)`` -- SubKeys: [$($v.SubKeys)]")
    }
    [void]$tail.AppendLine("")
}

$fixedScopeVerbs = $allVerbs | Where-Object {
    $_.ScopePath -notlike '*ProgID*' -and $_.ScopePath -notlike '*.ext*' -and
    $_.ScopePath -notlike '*SystemFileAssociations*'
}
$dupes = $fixedScopeVerbs | Group-Object VerbName | Where-Object { $_.Count -gt 1 }
if ($dupes.Count -gt 0) {
    [void]$tail.AppendLine("### Verbs Registered at Multiple Fixed Scopes (Dedup Candidates)")
    [void]$tail.AppendLine("")
    [void]$tail.AppendLine("Scanner should group these as one logical verb with multiple scopes.")
    [void]$tail.AppendLine("")
    foreach ($d in $dupes) {
        $scopes = ($d.Group | ForEach-Object { $_.ScopePath }) -join ', '
        [void]$tail.AppendLine("- **``$($d.Name)``** ($($d.Count)x): $scopes")
    }
    [void]$tail.AppendLine("")
}

[void]$tail.AppendLine("")
[void]$tail.AppendLine("## Manual Observations")
[void]$tail.AppendLine("")
[void]$tail.AppendLine("*Add any manual findings here after reviewing the report.*")
[void]$tail.AppendLine("")

# Append tail sections
$tail.ToString() | Out-File -FilePath $OutputPath -Encoding UTF8 -Append

Write-Host "  Report written to: $OutputPath" -ForegroundColor Green
Write-Host ""
