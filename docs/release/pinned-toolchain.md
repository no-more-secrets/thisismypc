# Preserved release toolchain

The release compiler is a separate Build Tools installation. Community can update
without replacing it. Release scripts select the exact manifest version, then
explicitly select its MSVC and Windows SDK. NativeAOT receives absolute linker
paths and disables its default newest-Visual-Studio discovery.

The version pins in `tools/reproducible-build-environment.json` remain unchanged.
`global.json` still requires .NET SDK 10.0.400 with roll-forward disabled.

## Machine locations

- Installation: `C:\Program Files\Microsoft Visual Studio\18\ThisIsMyPC-Pinned`.
- Offline archive: `C:\ProgramData\ThisIsMyPC-ReleaseTools`.
- Frozen channel: `VS-18.9.12120.119\layout\ChannelManifest.json` under the archive.
- SDK recovery: `dotnet-sdk-10.0.400-win-x64.zip` under the archive.

The archive contains 419 files, about 3.67 GB, outside repository cleanup paths.
The repository pins its inventory hash. Each inventory entry records a SHA-256
and file size. Microsoft's fixed bootstrapper signature was valid when downloaded;
the SDK zip matches Microsoft's published SHA-512. Source URLs and hashes are in
`tools/release-toolchain-archive.json`.

The Build Tools instance receives updates only from this frozen local channel.
Do not update this layout in place. A deliberate toolchain upgrade needs a new
archive and reproducibility checks before changing release pins.
This does not disable Windows Update or updates for the development IDE.

## Verify and recover

Verify the archive before using it:

```powershell
.\tools\test-release-toolchain-archive.ps1
```

Copy the entire archive to backup storage to survive disk loss. For recovery on
another machine, restore it to the same ProgramData location. A copy can be
verified elsewhere with the verifier's `-ArchiveRoot` argument.

From an elevated PowerShell window, restore Build Tools with:

```powershell
$layout = 'C:\ProgramData\ThisIsMyPC-ReleaseTools\VS-18.9.12120.119\layout'
& "$layout\vs_BuildTools.exe" --installPath 'C:\Program Files\Microsoft Visual Studio\18\ThisIsMyPC-Pinned' --noWeb --noUpdateInstaller --quiet --norestart --wait
```

The archived `Response.json` includes C++ tools, the Windows 26100 SDK, Clang,
and the frozen channel location. Installer exit code 3010 requires a restart;
0 means success. Do not redirect recovery to the online Stable channel.

If SDK 10.0.400 is missing, extract its verified zip into a dedicated directory
and place that directory first on PATH for the release shell. Do not overwrite
an existing SDK installation with an archive extraction.

Then check the actual installed tools:

```powershell
.\tools\test-reproducible-build-environment.ps1
.\tools\test-release-toolchain-selection.ps1 -CheckInstalled
```

Selection, repeated initialization, and archive checks pass in Windows
PowerShell 5.1 and PowerShell 7. The installed check covers JSON discovery of
multiple Visual Studio instances, including a newer Community installation.

The full check also pins Windows and Windows Installer versions. This archive
preserves build tools, not a Windows image. A later OS update can still require
a preserved Windows release VM or deliberate environment qualification.

Microsoft references: [fixed release bootstrappers](https://learn.microsoft.com/en-us/visualstudio/releases/2026/release-history),
[offline layouts](https://learn.microsoft.com/en-us/visualstudio/install/create-a-network-installation-of-visual-studio),
and [update channel configuration](https://learn.microsoft.com/en-us/visualstudio/install/use-command-line-parameters-to-install-visual-studio#modifysettings-command-and-command-line-parameters).
