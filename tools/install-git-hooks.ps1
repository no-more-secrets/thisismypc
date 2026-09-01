# Points this clone at the tracked hooks in tools/git-hooks (one-time setup).
# The pre-commit hook regenerates AGENTS.md and GEMINI.md whenever a commit
# touches CLAUDE.md or either twin, so the twins never drift from the master.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
git -C $repoRoot config core.hooksPath tools/git-hooks
if ($LASTEXITCODE -ne 0) { throw 'git config failed' }
Write-Host "core.hooksPath = tools/git-hooks for $repoRoot"
