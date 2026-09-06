# Points this clone at the tracked hooks in tools/git-hooks (one-time setup).
# The pre-commit hook maintains one-line CLAUDE.md and GEMINI.md imports of AGENTS.md.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
git -C $repoRoot config core.hooksPath tools/git-hooks
if ($LASTEXITCODE -ne 0) { throw 'git config failed' }
Write-Host "core.hooksPath = tools/git-hooks for $repoRoot"
