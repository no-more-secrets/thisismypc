# Contributing to ThisIsMyPC

Bug reports, screenshots, preview testing, and code changes all help.
You do not need to write code to report a problem.

## Report a problem

Use the [bug report](https://github.com/No-More-Secrets/thisismypc/issues/new?template=bug-report.md).
Include what you clicked, what you expected, and what happened.
Add your Windows edition and app version, or the source commit for a local build.
Remove personal information from screenshots and error text.

For a new setting or feature, use the [change proposal](https://github.com/No-More-Secrets/thisismypc/issues/new?template=change-proposal.md).
Describe the user-visible result first. Technical details can follow during the discussion.
Agree substantial changes in the issue before writing code.

## Set up your clone

Use Windows 11 x64, Git, and the exact .NET SDK in [global.json](global.json).
GitHub CLI is needed for the agent role check, issues, and pull requests.
Sign in with `gh auth login`.

To contribute, fork and clone:

```powershell
gh repo fork No-More-Secrets/thisismypc --clone
cd thisismypc
.\Setup.ps1
```

Setup checks prerequisites, initializes the OpenRGB submodule, installs hooks, verifies agent instructions, builds, and runs CI-safe tests.
It does not need administrator permission.

The C++ lighting engine is separate from the .NET solution.
It needs Visual Studio C++ tools; follow the [lighting build guide](docs/lighting-controllers.md#build).
NativeAOT release builds also need the native toolchain.
The ordinary .NET build does not require Visual Studio.

## Work with your agent

Open the clone in your coding agent and give it the issue or task you want to handle.
For example:

```text
Read AGENTS.md and investigate issue #123 in this clone.
Explain the cause, implement the agreed fix, and run the required checks.
Use the screenshot harness for UI changes. Prepare a pull request.
Do not apply changes to this PC as part of testing.
```

Replace the issue number with your issue.
[AGENTS.md](AGENTS.md) is the source for architecture, role, commit, and verification rules.
[CLAUDE.md](CLAUDE.md) and [GEMINI.md](GEMINI.md) import those same instructions.

## Build and test

```powershell
dotnet build --configuration Release
dotnet test --configuration Release --filter "Category!=Integration&Category!=Diagnostic"
```

CI-safe tests use fakes. Integration and Diagnostic tests can read or change the live system.
Run those only when the task requires them and you understand their effects.

For UI changes, use the headless screenshot harness described in [AGENTS.md](AGENTS.md#ui-work-use-the-sight-harness-not-sams-eyes).
Inspect the rendered images before submitting a change.
Keep test output under `artifacts/`.

Run a local preview when you want to try the app:

```powershell
dotnet run --project src/ThisIsMyPC.App
```

The app requests administrator permission for its Broker at startup.
Browsing, staging, and discard do not apply the staged settings.
See [preview testing](docs/testing/preview-testing.md).

## Submit a change

Keep the change focused on the agreed issue.
Use the pull request template and include verification results.
Attach screenshots for UI changes and identify anything left unverified.
The maintainer reviews the change and updates the backlog.

The project uses GPLv3, version 3 only. Retain upstream licenses and attribution when adapting other projects.
