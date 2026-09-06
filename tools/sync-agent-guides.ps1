# AGENTS.md is the sole instruction source. Maintain one-line native loaders.
# Run without -Check to repair loaders, or with -Check for read-only CI validation.
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$masterPath = Join-Path $repoRoot 'AGENTS.md'
if (-not (Test-Path -LiteralPath $masterPath -PathType Leaf) -or
    [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($masterPath))) {
    throw 'AGENTS.md must exist and contain the shared instructions.'
}
$drift = @()
foreach ($name in @('CLAUDE.md', 'GEMINI.md')) {
    $path = Join-Path $repoRoot $name
    $current = if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllText($path) } else { '' }
    if ($current -ceq "@AGENTS.md`n" -or $current -ceq "@AGENTS.md`r`n") {
        Write-Host "${name}: loader valid"
    } elseif ($Check) {
        $drift += $name
    } else {
        [IO.File]::WriteAllText($path, "@AGENTS.md`n", (New-Object Text.UTF8Encoding($false)))
        Write-Host "${name}: loader repaired"
    }
}
if ($drift.Count -gt 0) {
    Write-Host "Invalid instruction loaders: $($drift -join ', '). Run tools/sync-agent-guides.ps1."
    exit 1
}
