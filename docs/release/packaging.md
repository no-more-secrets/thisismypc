# Release packaging: machine scope

The app corresponds to the PC, not a user profile (AGENTS.md). Packaging follows:

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
  current\sq.version supplies the version. Stale MSI DisplayVersion values are
  never used for package selection.
  Welcome names its version and folder and offers Uninstall behind a confirm
  page; that runs Velopack's own `Update.exe uninstall --silent`, which is
  the uninstaller for this app (there is no unins000.exe). An older version
  updates in place (folder locked, button reads Update), the same full version
  reinstalls (REINSTALL=ALL REINSTALLMODE=vomus, or msiexec answers 1638),
  and a newer one blocks Next until it is removed. Prerelease labels are part
  of the version. An upgrade from test-07 to test-08 never uses reinstall mode.
  Windows Installer compares only the numeric MSI version. The normalized MSI
  includes equal numeric versions in its major-upgrade range, so it replaces
  the old prerelease product instead of registering a second product. A second
  detection row blocks that ambiguous transition when someone runs the MSI
  directly. The launcher performs the complete prerelease ordering and passes
  the guarded upgrade property. It runs
  the MSI quietly (`/qn`, `VELOPACK_INSTALLDIR`, verbose log under
  `%ProgramData%\ThisIsMyPC\logs`), removes the Public Desktop shortcut when
  unticked, and writes three untrusted behavior choices to the installing
  account's HKCU registry. The unelevated app imports and deletes those values
  on first start. `AutoStartService.Reconcile()` then turns the setting into
  the Run entry.
- **Native DLL versions survive upgrades.** Every rebuilt native DLL keeps a
  Windows file version. Windows Installer can reject an unversioned replacement
  when the installed component has a version, then remove the installed file
  while removing the old product. The dependency build checks SQLite's version
  before it creates the local package.
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
  reads the PE headers of the files about to ship. The release fails if the App,
  Broker, Service, installer, generated app launcher, execution stub, or updater
  lacks ASLR with a real relocation table, high-entropy VA, DEP, CFG with a populated
  target table, the /GS stack cookie, CET, or table-based x64 unwinding.
  It also rejects any writable executable section.
  Pinned source builds give Skia, HarfBuzz, and SQLite the same mitigations.
  ANGLE remains upstream because its shipped binary passes the complete gate.
  Velopack's three Windows helpers are rebuilt as x64 from its pinned 1.2.0 source.
  The local packaging patch preserves debug-directory pointers after resource edits.
  This keeps CET metadata valid in each generated app launcher.
  Stack guard pages are not a file property. Windows places one below every
  thread stack.
- **Nothing trusted goes through %TEMP%.** The installer hardens
  `%ProgramData%\ThisIsMyPC` (Administrators/SYSTEM, the app's own
  `DataDirectoryGuard`) before it writes the unpacked MSI or the two native
  libraries NativeAOT cannot fold in (libSkiaSharp, libHarfBuzzSharp; the
  csproj embeds them, `NativeBootstrap` unpacks and loads them by absolute
  path). A same-user non-elevated process can write to %TEMP% and would get
  our elevation by swapping a file there. If the DACL cannot be set, the
  installer stops with a message box before loading anything.
- **State is split by trust.** UI settings, history, sets, monitoring state,
  and logs live in `%LocalAppData%\ThisIsMyPC`. The elevated Broker never trusts
  those files as authority. Owner Mode consent, baseline, and journal state stay
  under the Administrators/SYSTEM-only `%ProgramData%\ThisIsMyPC` directory.
  `LegacyDataMigration` copies old `%APPDATA%\ThisIsMyPC` UI state once.
- **Updates**: every download is verified against the GPG-signed manifest before
  apply. Applying a per-machine update from the unelevated UI remains a required
  live release test.

## Building a release

```
.\tools\build-release.ps1 -Version 1.0.0
```

`tools/invoke-vpk.ps1` downloads the official pinned `vpk` package and checks its
hash. It installs the rebuilt helpers and patched packaging assembly in an artifact
cache before each pack. Run `tools/build-velopack-helpers.ps1` to reproduce those
four local files from the pinned Velopack source and Rust toolchain.

Official releases are NativeAOT only. The script publishes the App, elevated
Broker, and Session 0 Service as self-contained win-x64 binaries. The Broker
and Service sit beside the App in the package. The script then packs the MSI
and writes `SHA256SUMS`. Follow
`update-signing.md` for signing and upload.

All four release executables enable Arbitrary Code Guard before application
startup. The local Avalonia.Win32 and SkiaSharp packages under
`third-party/acg` replace runtime-generated native callback thunks with static
unmanaged callbacks. Rebuild them with `tools/build-acg-dependencies.ps1`.
Run `tools/AcgLauncher` from an elevated terminal to test loader-time ACG.
The launcher also rejects a window whose captured frame contains no visible pixels.
The App and Installer use Avalonia's software renderer in ACG builds. ANGLE
creates the window under strict ACG but presents black frames on the tested host.
`AotPublish=true` alone creates an unsigned NativeAOT diagnostic build without
ACG or CIG. Add `DynamicCodeGuard=true` to test ACG without signing.
Non-ACG development builds retain Avalonia's normal platform detection.
Release builds validate the four native DLLs against pinned SHA-256 values.
The App bakes their canonical hashes into its NativeAOT image. Signing changes
only the terminal Authenticode certificate table, so the same values remain valid.
The release build also rejects App, Broker, or Service PE identity fields that
still name a managed `.dll` module.

### Signing with SSL.com eSigner

Releases use SSL.com eSigner CKA in Automated Code Signing and Production mode.
Keep the account's malware blocker enabled. Install CKA 1.1.2 and load its master
key. The release script downloads CodeSignTool 1.3.3 from SSL.com into
`artifacts/tool-cache/esigner/` when the cache is empty.
The release gate checks every executable CKA runtime file against
`tools/esigner-signing-environment.json`. It also verifies the complete
CodeSignTool archive hash on every run before extraction.
This pin is important because the installed CKA runtime files are not themselves
Authenticode-signed.

Run the interactive launcher for a local signed or unsigned build:

```powershell
.\tools\start-release-build.ps1
```

The launcher prompts for every omitted option. Passed parameters skip their
matching prompts. `build-release.ps1` remains the noninteractive CI entry point.

Use the credential ID beside the eSigner code-signing certificate, not the
document eSeal ID. The local command prompts privately for the SSL.com account
password. CodeSignTool has no protected password-input channel, so its Java
process receives that password during each scan or MSI signing command. Use a
dedicated release machine with no untrusted same-user processes.
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
  -ESignerCredentialId $env:ESIGNER_CREDENTIAL_ID
```

That script converts `ESIGNER_PASSWORD` to a secure string and removes the
environment variable before starting any child process. This prevents build
tools, SignTool, and later children from inheriting it. `build-release.ps1`
refuses to start if the password is already in its environment.

The script repacks through Velopack's signing callback. Each installed EXE and
DLL is malware-scanned immediately before the pinned SignTool signs it. This
includes the four locally built native dependency DLLs and Velopack helpers.
CKA signs these PE files and the outer installer without another OTP.

The MSI uses CodeSignTool's integrated scan and sign command. Enter one current
eSigner OTP when it asks. This path is required because SSL.com's separate
`scan_code` and CKA path approves a different MSI digest than SignTool submits.
The integrated command scans and signs the same MSI byte sequence. Every object
is checked for signer, chain, timestamp, and thumbprint. The outer installer is
built at a temporary path, verified, and then replaces the unsigned installer.
`SHA256SUMS` is written only after the complete signed install tree matches the
preserved unsigned build.

The current NativeAOT package uses eleven SSL.com signing credits: nine package
PE files, the MSI, and the outer installer. Velopack batches paths into one
callback. This reduces authentication and network trips, but each object still
uses one signing credit. A catalog signature would remove embedded signatures
from these files and add catalog registration state. The pipeline uses embedded
signatures so Windows can verify every executable file directly.

The complete path was exercised on 2026-09-03 using source commit
`1dc1ff3f86262ae064cbc9dc3d7384bd6410924d` and test version
`0.0.1-signingtest.1`. SignTool reported a valid No More Secrets, LLC signature
and SSL.com timestamp. Removing the 8,072-byte certificate table produced the
exact unsigned SHA-256
`73049718503DE3A1CCFD4225CB31B6A501B7FB431A01922316BB5D45B7F67E4F`.

The all-PE path passed on 2026-09-07 with test version `1.0.1-test-06`.
All 20 signature locations across the release, MSI payload, and nupkg were
valid and timestamped. The signed-to-unsigned comparison produced release root
`0C84F2F4BD7F5FE2960C27CBBE982B16F26AD6DB0320ED534991E802A9BE1778`.

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
.\tools\build-release.ps1 -Version 1.0.0
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
only signature tables and PE sizes changed by nested signing. MsiFileHash values
for signed PE files are canonicalized because Authenticode changes those values.
Hash values for all non-PE files remain part of the canonical metadata. It then
expands the cabinet and canonicalizes every PE. Non-PE files compare byte for
byte.
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
  passed that day. The release now signs every installed PE, the
  MSI, and the outer installer while preserving public source verification
  through the canonical release tree. Builds without `-SignThumbprint` are
  unsigned test builds.
- `AppConstants.UpdateUrl` points at github.com/No-More-Secrets/thisismypc
  (public since 2026-09-01).
- NativeAOT: `build-release.ps1` publishes the App (~38 MB exe, zero
  trim warnings since the shared row templates gained compiled bindings) and
  the Session 0 Service (~6 MB exe, zero trim warnings, probed 2026-09-01:
  hosts and starts as a console process) native with Control Flow Guard. It
  is both or neither: they share one folder. The release script has no CoreCLR
  path. The remaining release gate is one full manual pass on an AOT build
  (every module page plus an apply, Owner Mode enable for the service). The
  install half is done: Sam installed AOT 0.1.0 and updated it to AOT 0.1.1 on
  2026-09-02, both clean. The in-app half is open.
