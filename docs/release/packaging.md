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
- **The download is `ThisIsMyPC-Installer-<version>.exe`** (`src/ThisIsMyPC.Installer`,
  Avalonia, NativeAOT, with a length-delimited MSI payload). Two reasons it exists.
  A bare per-machine MSI gets its UAC consent requested by the Installer
  service, not by the wizard window, so Windows parks the prompt in the
  taskbar and the dialog that follows can land off screen (seen 2026-09-01
  on a 4K display). The launcher carries `requireAdministrator`, so UAC is a
  normal modal before anything runs and msiexec then runs elevated with no
  desktop switch. And the Velopack wizard has no options; the launcher has
  pages: Welcome, License (GPLv3 with an accept step), Options (install
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
- **File properties** (Explorer, Details tab): description "ThisIsMyPC
  Installer", product version without the commit hash
  (`IncludeSourceRevisionInInformationalVersion` off in Directory.Build.props),
  original filename ending in .exe (the IL module is compiled as .exe with no
  apphost; NativeAOT emits the real one), and language English (United States).
  The C# compiler always writes the version block Language Neutral and no
  property changes that, so `tools/set-version-language.ps1` rewrites the block
  in the packed exe before signing. "Type: Application" is Explorer's label for
  every .exe and cannot be changed.
- **Exploit mitigations are a release gate.** `tools/check-binary-hardening.ps1`
  reads the PE headers of the files about to ship and build-release.ps1 fails
  if App, Service, or the installer lacks any of: ASLR with high-entropy VA,
  DEP, Control Flow Guard (GUARD_CF plus the CF function table), the /GS
  stack cookie, and table-based x64 unwinding. It also reports CET shadow
  stack compatibility (all three first-party exes have it) and the bundled
  native libraries: Skia and HarfBuzz ship without CFG and CET, which is
  upstream's build and noted here rather than hidden. Accepted (Sam,
  2026-09-01) because the app feeds them only text and its own fonts: no
  images, icons, or third-party fonts are decoded, and text shaping is a far
  smaller bug surface than the image and font parsers where those libraries
  have had CVEs. Revisit the day the app renders third-party images, icons,
  or fonts (publisher icons in the Software catalog, for example): then
  either CFG-built natives or decoding outside the elevated process. Stack
  guard pages are not a file property; Windows places one below every
  thread stack.
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
dotnet tool restore               # restores the repository-pinned vpk version
.\tools\build-release.ps1 -Version 1.0.0 -Aot
```

Official releases are NativeAOT only. The script publishes the App and the
Session 0 Service (self-contained, win-x64) into one staging directory (the
service exe must sit next to the app exe for Owner Mode enable), packs the MSI,
and writes `SHA256SUMS`. Then follow
`update-signing.md` for signing and upload.

### Signing with SSL.com eSigner

Releases use SSL.com eSigner CKA in Automated Code Signing and Production mode.
Keep the account's malware blocker enabled. Install CKA 1.1.2, load its master
key, and download the unmodified CodeSignTool 1.3.3 Windows zip from SSL.com.
The release gate checks every executable CKA runtime file against
`tools/esigner-signing-environment.json`. It also verifies the complete
CodeSignTool archive hash before extracting it into a new temporary directory.
This pin is important because the installed CKA runtime files are not themselves
Authenticode-signed.

Set the non-password inputs for the shell and run the signed build:

```powershell
$env:ESIGNER_USERNAME = 'your SSL.com account username'
$env:ESIGNER_CREDENTIAL_ID = 'the code-signing credential ID'
$env:ESIGNER_CODESIGNTOOL_ARCHIVE = 'C:\path\to\CodeSignTool-v1.3.3-windows.zip'
.\tools\build-release.ps1 -Version 1.0.0 `
  -Aot `
  -SignThumbprint 'the 40-character certificate thumbprint'
```

Use the credential ID beside the eSigner code-signing certificate, not the
document eSeal ID. The local command prompts privately for the SSL.com account
password. CodeSignTool has no protected password-input channel, so its Java
process still receives that password as an argument for the lifetime of the
scan. Use a dedicated release machine with no untrusted same-user processes.
For unattended CI, store `ESIGNER_PASSWORD` in the runner's secret store and
expose it only to the signing step. Never put the password, CKA master key, or
TOTP seed in the repository, workflow text, command history, artifacts, or logs.

CI must build unsigned before the secret-bearing process starts. In a separate
Windows signing step, expose the password and run:

```powershell
.\tools\sign-release-installer.ps1 `
  -AssetDirectory ".\artifacts\releases\$version" `
  -StagingDirectory ".\artifacts\staging\$version" `
  -InstallerStub ".\artifacts\staging\$version-installer\ThisIsMyPC-Installer.exe" `
  -Version $version `
  -SignThumbprint $thumbprint `
  -ESignerCredentialId $env:ESIGNER_CREDENTIAL_ID `
  -CodeSignToolArchive $env:ESIGNER_CODESIGNTOOL_ARCHIVE
```

That script converts `ESIGNER_PASSWORD` to a secure string and removes the
environment variable before starting any child process. This prevents build
tools, SignTool, and later children from inheriting it. `build-release.ps1`
refuses to start if the password is already in its environment.

The script repacks through Velopack's signing callback. Each exact first-party
file is malware-scanned immediately before the pinned SignTool signs it. It then
normalizes, scans, and signs the MSI, builds the outer bundle, and scans and
signs that bundle. Every object is checked for signer, chain, timestamp, and
thumbprint. `SHA256SUMS` is written only after the complete signed install tree
matches the preserved unsigned build.

Each official NativeAOT release uses six SSL.com signing credits: app, service,
Velopack app stub, Update.exe, MSI, and outer installer. Velopack batches up to
100 paths into one callback, which reduces authentication and network round
trips but does not reduce SSL.com's per-object credit count. A catalog signature
could cover many files with one credit, but those files would not carry
embedded signatures and catalog registration adds failure-prone
installer state. It saves nothing for the two-file NativeAOT payload, so the
release pipeline deliberately uses embedded signatures. Third-party binaries
retain their upstream signatures and are never re-signed as No More Secrets.

The complete path was exercised on 2026-09-03 using source commit
`1dc1ff3f86262ae064cbc9dc3d7384bd6410924d` and test version
`0.0.1-signingtest.1`. SignTool reported a valid No More Secrets, LLC signature
and SSL.com timestamp. Removing the 8,072-byte certificate table produced the
exact unsigned SHA-256
`73049718503DE3A1CCFD4225CB31B6A501B7FB431A01922316BB5D45B7F67E4F`.

Build inputs are locked: `global.json` selects the exact .NET SDK,
`.config/dotnet-tools.json` pins vpk, and each project commits its NuGet
`packages.lock.json`. Projects in a NativeAOT graph also commit
`packages.aot.lock.json`, because NativeAOT adds a different package graph.
Each lock file covers the only supported runtime, win-x64. Release
configuration restores fail on lock-file drift. After an intentional
dependency change, refresh and review both applicable lock-file diffs.

Avalonia's transitive `Avalonia.BuildServices` dependency is overridden as a
private assetless reference in every project whose dependency graph reaches
Avalonia. No telemetry task, collector, build target, or runtime assembly is
imported. This is enforced at restore rather than through a machine-specific
opt-out environment variable.

```
dotnet restore ThisIsMyPC.slnx --force-evaluate -p:RestoreLockedMode=false
dotnet restore src\ThisIsMyPC.Installer\ThisIsMyPC.Installer.csproj -r win-x64 --force-evaluate -p:AotPublish=true -p:RestoreLockedMode=false -m:1
```

The machine-installed native toolchain is locked separately in
`tools/reproducible-build-environment.json`. `build-release.ps1` refuses to
run unless the Windows servicing build, Windows Installer engine, .NET SDK,
Visual Studio, MSVC tools, link.exe, Windows SDK, and MsiDb.exe match exactly.
The MsiDb executable is also content-hashed. This is intentional: a newer
compatible build tool is still a different build input.

## Reproducing the installer

Check out the release tag, install the exact toolchain named in
`tools/reproducible-build-environment.json`, and build the tag's version:

```
git checkout v1.0.0
.\tools\build-release.ps1 -Version 1.0.0 -Aot
```

The unsigned release pipeline is byte-for-byte deterministic. Roslyn
determinism, a checkout-independent compiler path map, and locked inputs cover
managed code. Release PDBs are omitted
because Avalonia's Cecil XAML rewrite gives portable-PDB debug records a new
identifier on each invocation. Staging timestamps are fixed before packaging.
`normalize-msi.ps1` derives the MSI ProductCode and PackageCode from the
version and normalizes WiX summary, compound-file, and cabinet timestamps.
`normalize-pe-timestamps.ps1` clears the three wall-clock timestamps emitted
by the Windows native linker. Two clean builds of `0.0.1-repro` from identical
source snapshots in different checkout paths on 2026-09-02 produced identical
release assets before signing.

The easiest independent check, suitable for a coding agent, is:

```
.\tools\verify-release.ps1 `
  -ReleasedInstaller C:\Downloads\ThisIsMyPC-Installer-1.0.0.exe
```

It infers the version, recognizes the NativeAOT package shape, clones the exact
tag into a disposable directory, validates the pinned environment, builds,
compares, and deletes the disposable clone. It parses but never executes the
downloaded file. For automation, success is exit code 0 together with a line
beginning `Reproducible release verified:`. Every failed trust, environment,
structure, or content check terminates with a nonzero exit code. To compare
against an already prepared local build:

```
.\tools\compare-reproducible-installer.ps1 `
  -ReleasedInstaller .\ThisIsMyPC-Installer-1.0.0.exe `
  -LocalInstaller .\artifacts\releases\1.0.0\ThisIsMyPC-Installer-1.0.0.exe
```

The comparison requires valid, timestamped Authenticode from No More Secrets,
LLC on the outer installer, MSI, app, service, and Velopack helpers. The outer
format is `[launcher][MSI][0 to 7 zero padding bytes][72-byte footer][signature]`.
The footer records a magic value, version, MSI offset, length, and SHA-256. This
lets the verifier separate the launcher and MSI without loading or running
either. The installer refuses to start unless WinVerifyTrust validates its own
No More Secrets, LLC signature. It performs the same bounds and payload-hash
checks, then requires a valid No More Secrets, LLC signature on the extracted
MSI before invoking Windows Installer.

For each PE, `normalize-authenticode-pe.ps1` accepts only a terminal, aligned
sequence of revision 2 PKCS SignedData records. It rejects overlays and tables
overlapping section data, removes the certificate table, and zeros only the PE
checksum and Security directory entry that Authenticode excludes from its image
digest. MSI comparison exports deterministic logical metadata while excluding
only signature tables and first-party PE sizes changed by nested signing. All
other File-table columns and the complete MsiFileHash table remain part of the
canonical metadata. It
then expands the cabinet and canonicalizes each first-party PE. Third-party
files compare byte for byte, including any upstream signatures.
Missing, additional, or different paths fail with their names. Matching records
are sorted into one SHA-256 release root. The download is never modified.

First end-to-end unsigned build ran 2026-09-01 (`-Version 0.1.0`): MSI,
full nupkg, RELEASES, releases.win.json, assets.win.json, SHA256SUMS. The
Velopack library and the vpk tool are kept on the same version (1.2.0 in
`Directory.Packages.props` and `.config/dotnet-tools.json`; update both in one
change when it moves) because vpk warns on a mismatch. A rebuild of the same
version wipes the per-version output directory first; vpk refuses to pack over
an existing release.

## Open items before first release

- Publisher line is `NMS` (Sam, 2026-09-01), the short form of No More
  Secrets, LLC: the `-Authors` default in build-release.ps1 and the assembly
  Company in Directory.Build.props both use it, so the install path is
  `Program Files\NMS\ThisIsMyPC`. The OV certificate subject and the
  assembly Copyright carry the full legal name. Release contact for Defender
  submissions and cert validation: inquiries@no-more-secrets.com.
- Authenticode signing is ready: SSL.com issued the No More Secrets, LLC OV
  certificate through eSigner on 2026-09-03. The original outer-only path
  passed that day. The release now signs every first-party installed PE, the
  MSI, and the outer installer while preserving public source verification
  through the canonical release tree. Builds without `-SignThumbprint` are
  unsigned test builds.
- `AppConstants.UpdateUrl` points at github.com/No-More-Secrets/thisismypc
  (public since 2026-09-01).
- NativeAOT: `build-release.ps1 -Aot` publishes the App (~38 MB exe, zero
  trim warnings since the shared row templates gained compiled bindings) and
  the Session 0 Service (~6 MB exe, zero trim warnings, probed 2026-09-01:
  hosts and starts as a console process) native with Control Flow Guard. It
  is both or neither: they share one folder. The installer is always NativeAOT,
  and no CoreCLR release will be published. Passing `-Aot` is mandatory for a
  release. The remaining release gate is one full manual pass on an AOT build
  (every module page plus an apply, Owner Mode enable for the service). The
  install half is done: Sam installed AOT 0.1.0 and updated it to AOT 0.1.1 on
  2026-09-02, both clean. The in-app half is open.
