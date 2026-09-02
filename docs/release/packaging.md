# Release packaging: machine scope

The app corresponds to the PC, not a user profile (CLAUDE.md). Packaging follows:

- **Binaries in `C:\Program Files\`** (admin-only write; deep-research
  DLL-sideloading rule). Velopack's default per-user `%LocalAppData%` Setup.exe
  violates this, so releases ship ONLY the per-machine MSI
  (`vpk pack --msi --instLocation PerMachine`, WiX 5, installs to
  `Program Files\{publisher}\ThisIsMyPC`, requires elevation). No per-user
  Setup.exe, no portable zip. vpk always emits the Setup.exe; the release
  script deletes it after pack (the MSI is a complete install by itself,
  checked with `msiexec /a` extraction on 2026-09-01).
- **The download is `ThisIsMyPC-Install-<version>.exe`** (`src/ThisIsMyPC.Installer`,
  Avalonia, NativeAOT, the MSI embedded as a resource). Two reasons it exists.
  A bare per-machine MSI gets its UAC consent requested by the Installer
  service, not by the wizard window, so Windows parks the prompt in the
  taskbar and the dialog that follows can land off screen (seen 2026-09-01
  on a 4K display). The launcher carries `requireAdministrator`, so UAC is a
  normal modal before anything runs and msiexec then runs elevated with no
  desktop switch. And the Velopack wizard has no options; the launcher has
  pages: Welcome, License (GPLv2 with an accept step), Options (install
  folder with a Program Files warning, Desktop shortcut, start with Windows,
  automatic update checks), Installing, Done (launch when finished). When a
  copy is already installed (found through the Apps entry Update.exe
  registers, or Update.exe plus current\sq.version in the default folder),
  Welcome names its version and folder and offers Uninstall behind a confirm
  page; that runs Velopack's own `Update.exe uninstall --silent`, which is
  the uninstaller for this app (there is no unins000.exe). An older version
  updates in place (folder locked, button reads Update), the same version
  reinstalls (REINSTALL=ALL REINSTALLMODE=vomus, or msiexec answers 1638),
  and a newer one blocks Next until it is removed. It runs
  the MSI quietly (`/qn`, `VELOPACK_INSTALLDIR`, verbose log under
  `%ProgramData%\ThisIsMyPC\logs`), removes the Public Desktop shortcut when
  unticked, and writes the behavior choices through the app's own
  `SettingsService`; the app's `AutoStartService.Reconcile()` turns the
  setting into the Run entry at first start.
- **Nothing trusted goes through %TEMP%.** The installer hardens
  `%ProgramData%\ThisIsMyPC` (Administrators/SYSTEM, the app's own
  `DataDirectoryGuard`) before it writes the unpacked MSI or the two native
  libraries NativeAOT cannot fold in (libSkiaSharp, libHarfBuzzSharp; the
  csproj embeds them, `NativeBootstrap` unpacks and loads them by absolute
  path). A same-user non-elevated process can write to %TEMP% and would get
  our elevation by swapping a file there. If the DACL cannot be set, the
  installer stops with a message box before loading anything.
- **Mutable state in `%ProgramData%\ThisIsMyPC`** (settings, history.db, sets,
  monitoring state, drift baseline): one database for the machine. The app
  creates and DACL-hardens the folder at startup (Administrators/SYSTEM only);
  a profile folder would be defeatable because users own their profile
  directories. Pre-machine-scope builds stored data in `%APPDATA%\ThisIsMyPC`;
  `LegacyDataMigration` copies it across once at startup and leaves a marker.
- **Updates**: the always-elevated app runs Update.exe, which can write
  Program Files; update flow is identical to per-user Velopack. Every download
  is verified against the GPG-signed manifest (update-signing.md) before apply.

## Building a release

```
dotnet tool install -g vpk        # once
.\tools\build-release.ps1 -Version 1.0.0
```

The script publishes the App and the Session 0 Service (self-contained,
win-x64) into one staging directory (the service exe must sit next to the app
exe for Owner Mode enable), packs the MSI, and writes `SHA256SUMS`. Then follow
`update-signing.md` for signing and upload.

First end-to-end unsigned build ran 2026-09-01 (`-Version 0.1.0`): MSI,
full nupkg, RELEASES, releases.win.json, assets.win.json, SHA256SUMS. The
Velopack library and the vpk tool are kept on the same version (1.2.0 in
`Directory.Packages.props`; `dotnet tool update -g vpk` when it moves) because
vpk warns on a mismatch. A rebuild of the same version wipes the per-version
output directory first; vpk refuses to pack over an existing release.

## Open items before first release

- Publisher line is `NMS` (Sam, 2026-09-01), the short form of No More
  Secrets, LLC: the `-Authors` default in build-release.ps1 and the assembly
  Company in Directory.Build.props both use it, so the install path is
  `Program Files\NMS\ThisIsMyPC`. The OV certificate subject and the
  assembly Copyright carry the full legal name. Release contact for Defender
  submissions and cert validation: inquiries@no-more-secrets.com.
- Authenticode signing: the SSL.com OV certificate was purchased 2026-09-01;
  identity validation is underway and the hardware token is expected within
  about a week. On release day plug the token in, find the thumbprint with
  `Get-ChildItem Cert:\CurrentUser\My`, and run
  `build-release.ps1 -Version x.y.z -SignThumbprint <40 hex>`. vpk then signs
  every exe, dll, and the MSI with an SSL.com RFC 3161 timestamp, the script
  verifies each signature, and SHA256SUMS is computed over the signed files.
  Builds without `-SignThumbprint` are unsigned test builds. The GPG manifest
  layer works with or without Authenticode.
- `AppConstants.UpdateUrl` points at github.com/No-More-Secrets/thisismypc
  (public since 2026-09-01).
- NativeAOT works (probed 2026-08-31): `-p:AotPublish=true` (or
  `build-release.ps1 -Aot`) produces a ~34 MB native App exe with ZERO trim
  warnings after the two shared row templates gained compiled bindings
  (IToggleSettingRow); a smoke launch on the live machine started clean.
  Native link needs the VS installer dir on PATH (vswhere). Default stays
  CoreCLR until one full manual pass on an AOT build (every module page plus
  an apply); the Session 0 Service is unprobed and stays CoreCLR.
