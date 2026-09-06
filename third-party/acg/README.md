# NativeAOT ACG dependency patches

This directory contains the dependency changes required by strict ACG and native hardening.
The application uses the local packages through `NuGet.Config`.

Avalonia Win32 11.3.12 created native callback thunks from managed delegates.
The patch replaces them with static `UnmanagedCallersOnly` function pointers.
It also passes window instances through `CreateWindowEx` creation data.

SkiaSharp 2.88.9 built four managed callback tables from delegates.
The patch uses static Cdecl function pointers and blittable callback tables.

The native x64 builds also replace Skia, HarfBuzz, and SQLite.
They enable CFG, CET, ASLR, high-entropy VA, DEP, `/GS`, and table-based unwinding.
The Skia build disables unused Vulkan support. ANGLE stays upstream because it already has every checked mitigation.

The package archives retain untouched files from the official packages.
Only the two managed assemblies and three Windows x64 native libraries are rebuilt.
The official archives are SHA-256 checked before repacking.
The rebuilt archives use fixed timestamps, sorted entries, and pinned output hashes.
The native rebuild checks every exported function against the official library before packing.

Rebuild all local packages from their pinned source commits:

```powershell
.\tools\build-acg-dependencies.ps1
```

Test a published GUI under loader-time ACG from an elevated terminal:

```powershell
dotnet run --project .\tools\AcgLauncher -- .\artifacts\aot\acg-03\01-app\ThisIsMyPC.App.exe
```

For the service, add `--no-window` after the executable path.
Numbered verification outputs use `01-app`, `02-service`, and `03-installer-launcher`.
The third folder is only the launcher. A distributable portable installer also needs its appended signed MSI payload.

The local packages are unsigned. The release pipeline signs the final native
executables and validates their installed file tree.
