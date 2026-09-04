<#
.SYNOPSIS
    Removes regenerable build and test output from the working tree.

.DESCRIPTION
    All build and test output lives under one gitignored root, artifacts\,
    as artifacts\<type>\<build>\ (see the Build & test section of CLAUDE.md):

      releases\<version>       shippable release outputs
      staging\<version>        intermediate publish staging for a release build
      ui-shots\<suite>         sight-harness screenshots, one folder per suite
      aot\<name>               ad-hoc NativeAOT publishes
      diagnostics\<name>       one-off logs, dumps, audits, probes

    This empties artifacts\ and, with -IncludeBinObj, every project's bin and
    obj. It never touches tracked files, and every path it removes is rebuilt
    on demand: build-release.ps1 recreates releases and staging, the UI tests
    recreate ui-shots, and a normal build recreates bin and obj.

.PARAMETER IncludeBinObj
    Also remove every bin and obj under src, tests, and analyzers. The next
    build is then a full one.

.PARAMETER WhatIf
    List what would be removed and the space it holds, delete nothing.

.EXAMPLE
    .\tools\clean-build-output.ps1
    .\tools\clean-build-output.ps1 -IncludeBinObj
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$IncludeBinObj
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

function Get-SizeMB {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0.0 }
    $bytes = (Get-ChildItem $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    return [math]::Round(([double]$bytes) / 1MB, 1)
}

# The one output root. Its contents go; the directory itself stays so tools
# that assume it exists do not have to recreate it.
$artifacts = Join-Path $repoRoot 'artifacts'
$targets = @()
if (Test-Path $artifacts) {
    $targets += Get-ChildItem $artifacts -Force -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
}

if ($IncludeBinObj) {
    foreach ($top in 'src', 'tests', 'analyzers') {
        $root = Join-Path $repoRoot $top
        if (Test-Path $root) {
            $targets += Get-ChildItem $root -Recurse -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -in 'bin', 'obj' } |
                Select-Object -ExpandProperty FullName
        }
    }
}

$total = 0.0
foreach ($target in $targets) {
    if (-not (Test-Path $target)) { continue }
    $size = Get-SizeMB $target
    $total += $size
    $rel = $target.Substring($repoRoot.Length + 1)
    if ($PSCmdlet.ShouldProcess($rel, "remove ($size MB)")) {
        Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host ("  removed {0,-44} {1,8:N1} MB" -f $rel, $size)
    }
    else {
        Write-Host ("  would remove {0,-40} {1,8:N1} MB" -f $rel, $size)
    }
}

$verb = if ($WhatIfPreference) { 'Would reclaim' } else { 'Reclaimed' }
Write-Host ""
Write-Host ("{0} {1:N1} MB" -f $verb, $total)
