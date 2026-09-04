<#
.SYNOPSIS
    Removes regenerable build and test output from the working tree.

.DESCRIPTION
    Everything this deletes is gitignored and rebuilt on demand: the artifacts
    tree (release builds and staging, reproducible-build clones, the sight
    harness screenshots, diagnostic logs), the builds output, and, with
    -IncludeBinObj, every project's bin and obj.

    It never touches tracked files. build-release.ps1 recreates
    artifacts\releases and artifacts\release-staging, the UI tests recreate
    artifacts\ui-shots, and a normal build recreates bin and obj.

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
    param([string[]]$Paths)
    $bytes = 0L
    foreach ($p in $Paths) {
        if (Test-Path $p) {
            $bytes += (Get-ChildItem $p -Recurse -Force -File -ErrorAction SilentlyContinue |
                Measure-Object -Property Length -Sum).Sum
        }
    }
    return [math]::Round($bytes / 1MB, 1)
}

# Regenerable output roots. Contents go; the directory itself stays so tools
# that assume it exists do not have to recreate it.
$targets = @(
    (Join-Path $repoRoot 'artifacts'),
    (Join-Path $repoRoot 'builds')
)

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

$totalBefore = 0.0
foreach ($target in $targets) {
    if (-not (Test-Path $target)) { continue }
    $size = Get-SizeMB $target
    $totalBefore += $size
    $rel = $target.Substring($repoRoot.Length + 1)
    if ($PSCmdlet.ShouldProcess($rel, "remove ($size MB)")) {
        # Empty the roots we keep (artifacts, builds); delete bin/obj outright.
        $leaf = Split-Path $target -Leaf
        if ($leaf -in 'artifacts', 'builds') {
            Get-ChildItem $target -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        }
        else {
            Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host ("  removed {0,-40} {1,8:N1} MB" -f $rel, $size)
    }
    else {
        Write-Host ("  would remove {0,-36} {1,8:N1} MB" -f $rel, $size)
    }
}

$verb = if ($WhatIfPreference) { 'Would reclaim' } else { 'Reclaimed' }
Write-Host ""
Write-Host ("{0} {1:N1} MB" -f $verb, $totalBefore)
