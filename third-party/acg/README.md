# NativeAOT ACG dependency patches

This directory contains the dependency changes required by strict ACG and native hardening.
The application uses the local packages through `NuGet.Config`.

Avalonia Win32 11.3.12 created native callback thunks from managed delegates.
The patch replaces them with static `UnmanagedCallersOnly` function pointers.
It also passes window instances through `CreateWindowEx` creation data.

SkiaSharp 2.88.9 built four callback tables and nine operation callbacks from delegates.
The patches use static Cdecl function pointers and blittable callback fields.

The native x64 builds also replace Skia, HarfBuzz, and SQLite.
They enable CFG, CET, ASLR, high-entropy VA, DEP, `/GS`, and table-based unwinding.
The Skia build disables unused Vulkan support. ANGLE stays upstream because it already has every checked mitigation.
The SQLite build keeps its upstream file version so MSI upgrades do not discard it as an unversioned file.

The package archives retain untouched files from the official packages.
Only the two managed assemblies and three Windows x64 native libraries are rebuilt.
The official archives are SHA-256 checked before repacking.
The rebuilt archives use fixed timestamps, sorted entries, and pinned output hashes.
The native rebuild checks every exported function against the official library before packing.

Rebuild all local packages from their pinned source commits:

```powershell
.\tools\build-acg-dependencies.ps1
```

Publish an unsigned ACG diagnostic build without invoking the signing pipeline:

```powershell
dotnet publish .\src\ThisIsMyPC.App\ThisIsMyPC.App.csproj `
  -c Debug -r win-x64 --self-contained true `
  -p:AotPublish=true -p:DynamicCodeGuard=true -p:OS=Windows_NT `
  -o .\artifacts\aot\app-unsigned-acg-01 -m:1
```

Test that GUI under loader-time ACG from an elevated terminal:

```powershell
dotnet run --project .\tools\AcgLauncher -- .\artifacts\aot\app-unsigned-acg-01\ThisIsMyPC.App.exe
```

For the service, add `--no-window` after the executable path.
Numbered verification outputs use `01-app`, `02-service`, and `03-installer-launcher`.
The third folder is only the launcher. A distributable portable installer also needs its appended signed MSI payload.

`AotPublish=true` alone produces unsigned NativeAOT diagnostics without ACG or CIG.
`DynamicCodeGuard=true` adds ACG. The release pipeline adds ACG to all four
executables and adds CIG to the App. It signs only the final release tree.
