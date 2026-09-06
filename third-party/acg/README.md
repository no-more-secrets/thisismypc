# NativeAOT ACG dependency patches

This directory contains the two dependency changes required by strict ACG.
The application uses the local packages through `NuGet.Config`.

Avalonia Win32 11.3.12 created native callback thunks from managed delegates.
The patch replaces them with static `UnmanagedCallersOnly` function pointers.
It also passes window instances through `CreateWindowEx` creation data.

SkiaSharp 2.88.9 built four managed callback tables from delegates.
The patch uses static Cdecl function pointers and blittable callback tables.

Both package archives retain untouched files from the official packages.
Only `lib/net8.0/Avalonia.Win32.dll` and `lib/net6.0/SkiaSharp.dll` are rebuilt.
The official archives are SHA-256 checked before repacking.
The rebuilt archives use fixed timestamps, sorted entries, and pinned output hashes.

Rebuild both local packages from their pinned source commits:

```powershell
.\tools\build-acg-dependencies.ps1
```

Test a published GUI under loader-time ACG from an elevated terminal:

```powershell
dotnet run --project .\tools\AcgLauncher -- .\artifacts\aot\acg-final-app\ThisIsMyPC.App.exe
```

For the service, add `--no-window` after the executable path.

The local packages are unsigned. The release pipeline signs the final native
executables and validates their installed file tree.
