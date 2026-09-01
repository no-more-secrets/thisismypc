# One-time setup for a fresh clone. Safe to rerun. Works on Windows PowerShell
# 5.1 and pwsh. Does not need elevation; only the app itself and the
# Integration and Diagnostic test tiers do.
#
#   .\Setup.ps1              full: prerequisites, hooks, guide parity, role, build, CI tests
#   .\Setup.ps1 -SkipBuild   stop after the checks and hooks
#   .\Setup.ps1 -SkipTests   build but do not run the test suite
param(
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
Set-Location $repoRoot

$problems = @()
function Step([string]$title) { Write-Host ''; Write-Host "== $title" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "   ok   $text" }
function Warn([string]$text) { Write-Host "   warn $text" -ForegroundColor Yellow }
function Fail([string]$text) { Write-Host "   FAIL $text" -ForegroundColor Red; $script:problems += $text }

Step 'Prerequisites'

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core' -and $env:OS -ne 'Windows_NT') {
    Fail 'This is a Windows-only project; run Setup.ps1 on Windows.'
}

$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) { Ok "git $((git --version) -replace 'git version ', '')" } else { Fail 'git not found on PATH' }

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $sdks = & dotnet --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }
    if ($sdks) { Ok ".NET SDK $(($sdks | Select-Object -Last 1) -replace ' .*', '')" }
    else { Fail '.NET 10 SDK not installed (dotnet --list-sdks shows no 10.x). https://dotnet.microsoft.com/download/dotnet/10.0' }
} else {
    Fail 'dotnet not found on PATH. Install the .NET 10 SDK.'
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    & gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Ok 'GitHub CLI signed in' }
    else { Warn 'GitHub CLI installed but not signed in: run gh auth login. Needed for the role check, issues, and PRs.' }
} else {
    Warn 'GitHub CLI (gh) not found. Agents use it for the owner/contributor role check, issues, and PRs. https://cli.github.com'
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) { Ok 'Visual Studio installer present (NativeAOT native link available)' }
else { Warn 'No Visual Studio installer found; NativeAOT publish needs the VC++ toolchain. Ordinary builds and tests do not.' }

if (Get-Command vpk -ErrorAction SilentlyContinue) { Ok 'vpk (Velopack) present' }
else { Warn 'vpk not installed; only needed to pack releases: dotnet tool install -g vpk' }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevated = (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($elevated) { Ok 'Elevated shell (Integration and Diagnostic tests can run here)' }
else { Warn 'Not elevated. Fine for setup and CI-safe tests; the app and the Integration/Diagnostic tiers need an admin terminal.' }

Step 'Git hooks'
& (Join-Path $repoRoot 'tools\install-git-hooks.ps1')

Step 'Agent guide parity (AGENTS.md, GEMINI.md from CLAUDE.md)'
& (Join-Path $repoRoot 'tools\sync-agent-guides.ps1') -Check
if ($LASTEXITCODE -ne 0) {
    Warn 'Twins were stale; regenerating.'
    & (Join-Path $repoRoot 'tools\sync-agent-guides.ps1')
}

Step 'Role'
$role = 'contributor'
if ($gh) {
    $perm = & gh repo view No-More-Secrets/thisismypc --json viewerPermission -q .viewerPermission 2>$null
    if ($LASTEXITCODE -eq 0 -and ($perm -eq 'ADMIN' -or $perm -eq 'WRITE')) { $role = 'owner' }
}
$origin = & git remote get-url origin 2>$null
if ($origin -match 'samboland/thisismypc') { $role = 'owner' }
if ($role -eq 'owner') {
    Ok 'Owner session: commit straight to main, push after every commit, keep the backlog current.'
} else {
    Ok 'Contributor session: never commit to main. Issue first for anything beyond a small fix, branch, then PR against No-More-Secrets/thisismypc.'
}

if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host "Setup stopped: $($problems.Count) problem(s) above." -ForegroundColor Red
    exit 1
}

if (-not $SkipBuild) {
    Step 'Restore and build (Release)'
    & dotnet build --configuration Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail 'Build failed'; exit 1 }
    Ok 'Build clean'

    if (-not $SkipTests) {
        Step 'CI-safe tests'
        & dotnet test --configuration Release --no-build -nologo -v q --filter 'Category!=Integration&Category!=Diagnostic'
        if ($LASTEXITCODE -ne 0) { Fail 'Tests failed'; exit 1 }
        Ok 'CI-safe suite green'
    }
}

Step 'Next'
Write-Host '   Rules for agents:     CLAUDE.md (master), AGENTS.md (Codex), GEMINI.md (Antigravity)'
Write-Host '   Docs:                 docs/README.md'
Write-Host '   Work list:            docs/planning/refinement-backlog.md (owner) or a GitHub issue (contributor)'
Write-Host '   UI verification:      dotnet test tests/ThisIsMyPC.App.UiTests --configuration Release --filter "Category!=Diagnostic"'
Write-Host '   Live-system tests:    elevated terminal, dotnet test --filter "Category=Integration"'
