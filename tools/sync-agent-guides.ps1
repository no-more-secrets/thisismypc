# Keeps AGENTS.md (Codex) and GEMINI.md (Antigravity) in parity with CLAUDE.md.
# CLAUDE.md is the master copy. Each twin is: a generated banner, the master
# body verbatim (only the H1 renamed), then a vendor-specific appendix that
# lives below a marker line and is preserved across syncs.
#
#   tools\sync-agent-guides.ps1          regenerate both twins
#   tools\sync-agent-guides.ps1 -Check   exit 1 if either twin is out of parity (CI)
#
# Runs on Windows PowerShell 5.1 and pwsh.
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$masterPath = Join-Path $repoRoot 'CLAUDE.md'
$marker = '<!-- vendor-specific: everything below this line is kept by tools/sync-agent-guides.ps1; everything above is generated from CLAUDE.md -->'

$twins = @(
    @{
        Name    = 'AGENTS.md'
        Vendor  = 'Codex'
        Default = @(
            '## Codex notes',
            '',
            '- Where the master says `/code-review`, run a fresh-context review with a new',
            '  session or a review sub-task; the bar is the same: findings fixed before commit.',
            '- Where the master says to read screenshots as images, open the PNGs under',
            '  `artifacts/ui-shots/` with your image input; never verify UI from the XAML.',
            '- The `gh` role check in "How work runs here" applies unchanged.'
        )
    },
    @{
        Name    = 'GEMINI.md'
        Vendor  = 'Antigravity'
        Default = @(
            '## Antigravity notes',
            '',
            '- Where the master says `/code-review`, run a fresh-context review with a new',
            '  session or a review agent; the bar is the same: findings fixed before commit.',
            '- Where the master says to read screenshots as images, open the PNGs under',
            '  `artifacts/ui-shots/` with your image input; never verify UI from the XAML.',
            '- The `gh` role check in "How work runs here" applies unchanged.'
        )
    }
)

function Read-Text([string]$path) {
    return [System.IO.File]::ReadAllText($path)
}

function Write-Text([string]$path, [string]$text) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
}

function Get-Appendix([string]$path, [string[]]$default) {
    if (Test-Path $path) {
        $existing = Read-Text $path
        $index = $existing.IndexOf($marker)
        if ($index -ge 0) {
            return $existing.Substring($index + $marker.Length).TrimStart("`r", "`n")
        }
    }
    return (($default -join "`n") + "`n")
}

function Build-Twin([hashtable]$twin, [string]$master) {
    $newline = if ($master.Contains("`r`n")) { "`r`n" } else { "`n" }
    $banner = @(
        "<!-- GENERATED from CLAUDE.md by tools/sync-agent-guides.ps1. Do not edit above the marker; edit CLAUDE.md and rerun the script. -->",
        ''
    ) -join $newline
    $body = $master -replace '^# CLAUDE\.md', ('# ' + $twin.Name)
    $body = $body.TrimEnd("`r", "`n")
    $appendix = (Get-Appendix (Join-Path $repoRoot $twin.Name) $twin.Default) -replace "`r?`n", $newline
    return $banner + $newline + $body + $newline + $newline + $marker + $newline + $newline + $appendix
}

$master = Read-Text $masterPath
$drift = @()

foreach ($twin in $twins) {
    $path = Join-Path $repoRoot $twin.Name
    $expected = Build-Twin $twin $master
    $current = if (Test-Path $path) { Read-Text $path } else { '' }

    if ($current -eq $expected) {
        Write-Host "$($twin.Name): in parity"
        continue
    }

    if ($Check) {
        $drift += $twin.Name
    } else {
        Write-Text $path $expected
        Write-Host "$($twin.Name): regenerated"
    }
}

if ($Check -and $drift.Count -gt 0) {
    Write-Host "Out of parity with CLAUDE.md: $($drift -join ', '). Run tools/sync-agent-guides.ps1 and commit."
    exit 1
}
