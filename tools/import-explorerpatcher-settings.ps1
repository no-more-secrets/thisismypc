<#
.SYNOPSIS
    Regenerates the ExplorerPatcher settings catalog from ExplorerPatcher's own
    source, pinned to one commit.

.DESCRIPTION
    ExplorerPatcher keeps every setting it exposes in a single annotated .reg
    manifest (ep_gui/resources/settings.reg): the key, the value name, the
    default, the control type, the option list, and the section conditions.
    Labels live in a string table (ep_gui/resources/lang/ep_gui.en-US.rc)
    referenced through the numeric ids in ep_gui/resources/*.h.

    This script reads those three files and writes
    src/ThisIsMyPC.Modules.Shell/Data/explorerpatcher-settings.json, which the
    app embeds and renders as ordinary reversible registry rows. Nothing calls
    into ExplorerPatcher: it watches its own keys with RegNotifyChangeKeyValue
    (see its SettingsMonitor.c), so writing the value is the whole interface.

    Settings the app already renders from its own readers are excluded, so no
    two rows ever write the same value.

.PARAMETER Commit
    ExplorerPatcher commit to import from. Pin it; do not track master.

.PARAMETER SourceRoot
    Optional local ExplorerPatcher clone to read instead of downloading.

.EXAMPLE
    .\tools\import-explorerpatcher-settings.ps1
#>
[CmdletBinding()]
param(
    [string]$Commit = '0a88a6e0ef6b1752fea36e581cffff1097e862b0',
    [string]$SourceRoot,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if (-not $OutputPath) {
    $OutputPath = Join-Path $PSScriptRoot '..\src\ThisIsMyPC.Modules.Shell\Data\explorerpatcher-settings.json'
}

# Pairs the app already renders from its own readers; importing them again
# would put two rows on the same registry value.
$AlreadyOurs = @(
    'Hidden', 'HideFileExt', 'ShowSuperHidden', 'SeparateProcess',
    'ShowSyncProviderNotifications', 'LaunchTo', 'NavPaneShowAllFolders',
    'NavPaneExpandToCurrentFolder', 'UseCompactMode', 'AutoCheckSelect',
    'ShowRecent', 'ShowFrequent', 'EnthusiastMode', 'HideMergeConflicts',
    'PersistBrowsers', 'ShowSecondsInSystemClock', 'ShowTaskViewButton',
    'TaskbarEndTask', 'Start_IrisRecommendations', 'Start_AccountNotifications',
    'SnapAssist', 'DisallowShaking', 'ShortcutNameTemplate',
    'TaskbarAl', 'TaskbarDa', 'SearchboxTaskbarMode', 'TaskbarGlomLevel'
)

# ExplorerPatcher pages that configure its own updater, its uninstaller, or
# show version text. Those are not Windows settings and are left to its own UI.
$SkipPages = @('Updates', 'Settings and uninstall', 'About')

# Which tab of the app's Explorer page each ExplorerPatcher page lands on.
$PageToSection = @{
    'Taskbar'         = 'Taskbar'
    'System tray'     = 'Taskbar'
    'Weather'         = 'Taskbar'
    'File Explorer'   = 'FileExplorer'
    'Start menu'      = 'StartMenu'
    'Window switcher' = 'Desktop'
    'Spotlight'       = 'Desktop'
    'Other'           = 'General'
    'Advanced'        = 'General'
}

function Get-EpFile {
    param([string]$RelativePath)
    if ($SourceRoot) {
        $local = Join-Path $SourceRoot $RelativePath
        if (-not (Test-Path $local)) { throw "Missing $local" }
        return [System.IO.File]::ReadAllBytes($local)
    }
    $uri = "https://raw.githubusercontent.com/valinet/ExplorerPatcher/$Commit/$RelativePath"
    Write-Host "  fetching $RelativePath"
    return (Invoke-WebRequest -Uri $uri -UseBasicParsing).Content
}

function ConvertFrom-EpBytes {
    param($Bytes)
    if ($Bytes -is [string]) { return $Bytes }
    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) {
        return [System.Text.Encoding]::UTF8.GetString($Bytes, 3, $Bytes.Length - 3)
    }
    if ($Bytes.Length -ge 2 -and $Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) {
        return [System.Text.Encoding]::Unicode.GetString($Bytes)
    }
    return [System.Text.Encoding]::UTF8.GetString($Bytes)
}

Write-Host "Importing ExplorerPatcher settings from commit $Commit"

$settingsReg = ConvertFrom-EpBytes (Get-EpFile 'ep_gui/resources/settings.reg')
$langRc = ConvertFrom-EpBytes (Get-EpFile 'ep_gui/resources/lang/ep_gui.en-US.rc')
$headers = @(
    (ConvertFrom-EpBytes (Get-EpFile 'ep_gui/resources/EPSettingsResources.h')),
    (ConvertFrom-EpBytes (Get-EpFile 'ep_gui/resources/EPSharedResources.h')),
    (ConvertFrom-EpBytes (Get-EpFile 'ep_gui/resources/resource.h'))
)

# numeric id -> IDS_ name (first header wins, matching the include order)
$idToName = @{}
foreach ($header in $headers) {
    foreach ($m in [regex]::Matches($header, '#define\s+(\w+)\s+(\d+)')) {
        if (-not $idToName.ContainsKey($m.Groups[2].Value)) {
            $idToName[$m.Groups[2].Value] = $m.Groups[1].Value
        }
    }
}

# IDS_ name -> English text
$nameToText = @{}
foreach ($m in [regex]::Matches($langRc, '(?m)^\s+(IDS_\w+)\s+"((?:[^"]|"")*)"')) {
    $nameToText[$m.Groups[1].Value] = $m.Groups[2].Value -replace '""', '"'
}

function Resolve-EpText {
    param([string]$Line)
    return [regex]::Replace($Line, '%R:(\d+)%', {
            param($m)
            $name = $idToName[$m.Groups[1].Value]
            if ($name -and $nameToText.ContainsKey($name)) { return $nameToText[$name] }
            return ''
        })
}

$settings = [System.Collections.Generic.List[object]]::new()
$page = ''
$heading = ''
$key = ''
$sectionStack = [System.Collections.Generic.List[string]]::new()
$pendingKind = $null
$pendingLabel = ''
$pendingOptions = [System.Collections.Generic.List[object]]::new()
$skipped = 0

foreach ($rawLine in ($settingsReg -split "`r?`n")) {
    $line = $rawLine.Trim()
    if (-not $line) { continue }

    if ($line.StartsWith('[')) {
        $key = $line.Trim('[', ']') `
            -replace '^HKEY_CURRENT_USER', 'HKCU' `
            -replace '^HKEY_LOCAL_MACHINE', 'HKLM' `
            -replace '^HKEY_CLASSES_ROOT', 'HKCR'
        continue
    }

    if ($line.StartsWith(';T ')) {
        $page = (Resolve-EpText $line.Substring(3)).Trim()
        $heading = ''
        $pendingKind = $null
        continue
    }

    # ";t" and ";a" put a sentence above a run of controls; ";q" ends that run
    # (ExplorerPatcher's own reader resets its last heading there). A control
    # label starting lower-case finishes the heading's sentence.
    if ($line -match '^;[ta]\s+(.*)$') {
        $text = (Resolve-EpText $Matches[1]).Trim()
        $heading = if ($heading) { "$heading $text" } else { $text }
        continue
    }

    if ($line -eq ';q') {
        $heading = ''
        continue
    }

    if ($line.StartsWith(';s ')) {
        $parts = $line.Substring(3).Split(' ', 2)
        $condition = if ($parts.Count -gt 1) { $parts[1].Trim() } else { '' }
        $sectionStack.Add($condition)
        continue
    }

    if ($line.StartsWith(';g ')) {
        if ($sectionStack.Count -gt 0) { $sectionStack.RemoveAt($sectionStack.Count - 1) }
        continue
    }

    # ;b label            boolean, on = 1
    # ;i label            boolean, on = 0 (the label names what the value hides)
    # ;c N label / ;z N label   choice with N options
    # ;x value label      one option of the choice being built
    if ($line -match '^;([biczx])\s+(.*)$') {
        $marker = $Matches[1]
        $rest = $Matches[2]

        if ($marker -eq 'x') {
            if ($rest -match '^(-?\d+)\s+(.*)$') {
                $pendingOptions.Add([ordered]@{
                        value = [int]$Matches[1]
                        name  = (Resolve-EpText $Matches[2]).Trim()
                    })
            }
            continue
        }

        $pendingOptions.Clear()
        if ($marker -eq 'c' -or $marker -eq 'z') {
            if ($rest -match '^\d+\s+(.*)$') { $rest = $Matches[1] }
            $pendingKind = 'choice'
        }
        else {
            $pendingKind = if ($marker -eq 'i') { 'invertedToggle' } else { 'toggle' }
        }
        $pendingLabel = (Resolve-EpText $rest).Trim()
        continue
    }

    # A value line closes whatever control was being described.
    if ($line -match '^"([^"]+)"=(.*)$') {
        $valueName = $Matches[1]
        $data = $Matches[2].Trim()

        if (-not $pendingKind) { continue }
        $kind = $pendingKind
        $label = $pendingLabel
        $options = @($pendingOptions.ToArray())
        $pendingKind = $null
        $pendingOptions.Clear()

        if ($SkipPages -contains $page) { continue }
        if ($AlreadyOurs -contains $valueName) { $skipped++; continue }
        if ($data -notmatch '^dword:([0-9a-fA-F]{8})$') { continue }   # strings need their own row type
        # Capture now: the label tidying below runs its own matches over $Matches.
        $defaultValue = [Convert]::ToInt32($Matches[1], 16)

        $requiresRestart = $label.EndsWith('*')
        $label = ($label -replace '\*+$', '').Trim()
        # "&&" is menu-escaping in the source strings.
        $label = $label -replace '&&', '&'
        $groupText = ($heading -replace '\*+$', '').Trim() -replace '&&', '&'
        if ($label -match '^%PLACEHOLDER') {
            # ExplorerPatcher ships a couple of strings its own table never fills in.
            $label = [regex]::Replace($valueName, '(?<!^)([A-Z])', ' $1')
        }
        if (-not $label) { continue }

        # A label that opens lower-case is the tail of the heading's sentence.
        if ($groupText -and $label -cmatch '^[a-z]') {
            $label = ($groupText.TrimEnd(':') + ' ' + $label).Trim()
            $groupText = ''
        }

        # The innermost non-empty section condition governs the row.
        $condition = ''
        for ($i = $sectionStack.Count - 1; $i -ge 0; $i--) {
            if ($sectionStack[$i]) { $condition = $sectionStack[$i]; break }
        }

        $settings.Add([ordered]@{
                id        = "ep:$valueName"
                name      = $label
                group     = $groupText
                page      = $page
                section   = $PageToSection[$page]
                key       = $key
                value     = $valueName
                kind      = $kind
                default   = $defaultValue
                restart   = $requiresRestart
                condition = $condition
                options   = $options
            })
        continue
    }

    # Any other comment ends a control that never reached a value line.
    if ($line.StartsWith(';') -and -not ($line -match '^;[a-z]\s')) { continue }
}

# Ids must be unique; ExplorerPatcher lists a couple of values twice for
# different Windows versions, and the first occurrence is the one that applies.
$unique = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new()
$dupes = 0
foreach ($setting in $settings) {
    if ($seen.Add("$($setting.key)|$($setting.value)")) { $unique.Add($setting) } else { $dupes++ }
}

$missingSection = @($unique | Where-Object { -not $_.section })
if ($missingSection.Count -gt 0) {
    throw "No tab mapped for page(s): $(($missingSection | ForEach-Object { $_.page } | Sort-Object -Unique) -join ', ')"
}

$document = [ordered]@{
    _license  = 'Settings definitions imported from ExplorerPatcher (https://github.com/valinet/ExplorerPatcher), GNU General Public License v2.0, Copyright (c) valinet. Regenerate with tools/import-explorerpatcher-settings.ps1.'
    _commit   = $Commit
    _imported = (Get-Date -Format 'yyyy-MM-dd')
    settings  = $unique
}

$json = $document | ConvertTo-Json -Depth 6
$outFull = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $outFull) | Out-Null
[System.IO.File]::WriteAllText($outFull, $json, (New-Object System.Text.UTF8Encoding $false))

Write-Host ''
Write-Host "Wrote $($unique.Count) settings to $outFull"
Write-Host "  skipped $skipped already rendered by the app's own readers, $dupes duplicate value(s)"
$unique | Group-Object section | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-14} {1,3}" -f $_.Name, $_.Count)
}
