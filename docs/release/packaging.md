# Release packaging: machine scope

The app corresponds to the PC, not a user profile (CLAUDE.md). Packaging follows:

- **Binaries in `C:\Program Files\`** (admin-only write; deep-research
  DLL-sideloading rule). Velopack's default per-user `%LocalAppData%` Setup.exe
  violates this, so releases ship ONLY the per-machine MSI
  (`vpk pack --msi --instLocation PerMachine`, WiX 5, installs to
  `Program Files\{publisher}\ThisIsMyPC`, requires elevation). No per-user
  Setup.exe, no portable zip.
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
  (the public repo, live since 2026-09-01 with the scrubbed history). The
  former private repo samboland/thisismypc is frozen as an archive.
- NativeAOT works (probed 2026-08-31): `-p:AotPublish=true` (or
  `build-release.ps1 -Aot`) produces a ~34 MB native App exe with ZERO trim
  warnings after the two shared row templates gained compiled bindings
  (IToggleSettingRow); a smoke launch on the live machine started clean.
  Native link needs the VS installer dir on PATH (vswhere). Default stays
  CoreCLR until one full manual pass on an AOT build (every module page plus
  an apply); the Session 0 Service is unprobed and stays CoreCLR.
