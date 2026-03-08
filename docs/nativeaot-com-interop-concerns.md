# NativeAOT and COM Interop: Architectural Concerns

## The Core Problem

Traditional .NET COM interop relies on runtime code generation: `Marshal.GetTypeFromCLSID`, `Activator.CreateInstance`, and dynamic Runtime Callable Wrapper (RCW) creation. NativeAOT strips all of this because there is no JIT compiler in the final binary. Anything depending on reflection-based marshalling will fail silently or throw at runtime.

## Affected Surfaces in ThisIsMyPC

| Module / Subsystem | COM Dependency | Interop Mechanism |
|---|---|---|
| **Context Menu Enumeration** | `IContextMenu`, `IShellExtInit` — QueryInterface against shell extension handler DLLs resolved from `shellex\ContextMenuHandlers` CLSIDs | In-process COM activation, vtable calls |
| **Scheduled Task Management** | `ITaskService` — COM-activated via `CoCreateInstance` with CLSID `{0f87369f-a4e5-4cfc-bd3e-73e6154572dd}` | Out-of-process COM (Task Scheduler service) |
| **WMI Queries (System Info, ASUS ATKACPI)** | `IWbemLocator` / `IWbemServices` — all WMI access is COM-based | Out-of-process COM (WMI service) |
| **Shell Extension Handler Resolution** | `HKCR\CLSID\{GUID}\InProcServer32` lookup, DLL loading, interface negotiation | In-process COM activation |

## The Path Forward: `ComWrappers`

Since .NET 5+, `System.Runtime.InteropServices.ComWrappers` replaces the automatic RCW/CCW machinery. COM vtable layouts are defined manually and marshalling is handled explicitly. This is fully AOT-compatible because everything is statically known at compile time.

**CsWin32 helps here** — it can source-generate AOT-friendly COM interface projections for well-known Win32 COM interfaces present in the Windows SDK metadata. More obscure interfaces, or any late-bound COM activation, may require hand-rolled `ComWrappers` implementations.

## WMI: A Separate Problem

`System.Management` (the traditional WMI namespace in .NET) is **not AOT-compatible**. Two alternatives exist:

1. **Direct COM against `IWbemLocator`/`IWbemServices`** — fully AOT-safe but requires manual vtable definitions and marshalling for every WMI query.
2. **`Microsoft.Management.Infrastructure` (MI/CIM)** — the newer Microsoft stack with better AOT prospects and a different API surface. Preferred path if it covers the needed WMI classes (particularly `AsusAtkWmi_WMNB` in `root\WMI`).

Either way, the `System.Management` dependency must be eliminated for NativeAOT to work.

## Enforcement Layer Implications (Epic 26)

The sprint change proposal introduced an enforcement-aware mutation layer — many Win11 settings require more than a registry write. Companion services must be disabled, GPCache entries cleared, and reversion vectors accounted for. This has two NativeAOT implications:

1. **Service Control Manager API** — `OpenSCManager`, `OpenService`, `ChangeServiceConfig` are all Win32 P/Invoke (Layer 1 in the architecture). Fully AOT-safe via CsWin32. No COM involvement.

2. **GPCache clearing** — Group Policy cache manipulation may require `IGroupPolicyObject` COM interface or direct registry deletion under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies`. If the COM path is needed, it's another `ComWrappers` candidate. If direct registry deletion suffices, no additional interop surface.

The enforcement layer itself is a Core abstraction — it orchestrates multi-step mutations but doesn't introduce new COM dependencies beyond what the modules already use.

## Session 0 Service Implications (Epic 28)

The drift watchdog runs as a SYSTEM-level Windows service in Session 0 with named pipe IPC. NativeAOT considerations:

1. **The service binary is a separate executable.** It can make its own NativeAOT/self-contained decision independently of the GUI. If the GUI ships NativeAOT but the service proves too painful (e.g., it needs WMI queries for drift detection), the service can ship as self-contained single-file without affecting the GUI's deployment model.

2. **Named pipe IPC** — `CreateNamedPipe`, `ConnectNamedPipe`, `ReadFile`, `WriteFile` are all Win32 P/Invoke. Fully AOT-safe. The security primitives (`FILE_FLAG_FIRST_PIPE_INSTANCE`, `SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION`) are flag constants, not COM.

3. **WMI in the service** — if drift detection needs to compare WMI-sourced system state, the same `System.Management` elimination applies here. The service would need `Microsoft.Management.Infrastructure` or direct COM against `IWbemLocator`.

4. **PawnIO integration (Phase 2+)** — the service will eventually broker IOCTL dispatch to the kernel driver. `DeviceIoControl` is Win32 P/Invoke. AOT-safe.

## Recommended Spike

The architecture exists but is being updated per the sprint change proposal. This spike remains the right first validation:

1. Read a CLSID from `HKCR\*\shellex\ContextMenuHandlers\<handler>`
2. Resolve the DLL path from `HKCR\CLSID\{GUID}\InProcServer32`
3. Activate the COM object via `CoCreateInstance`
4. Call `IContextMenu::QueryContextMenu` to enumerate the handler's menu items

If this works cleanly under NativeAOT with `ComWrappers`, everything else (ITaskService, WMI) is a known-quantity extension of the same pattern.

**If it proves too painful:** a self-contained single-file publish (which bundles the .NET runtime but preserves full COM interop) is the pragmatic fallback. The user-facing tradeoff is a larger binary (~60–80MB vs. ~15–25MB for NativeAOT) and a marginally slower cold start, but no functional limitations. This is a valid v1.0 decision that doesn't close the door on NativeAOT for a future release.
