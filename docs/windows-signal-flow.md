# Windows Kernel & Environment: Signal Flow Reference

**ThisIsMyPC Internal Documentation**
**Sam Boland — March 2026 — Living Document**

---

> This document maps the signal flow from power-on through running desktop, with emphasis on where the Windows registry acts as the control plane. Intended as a discrete engineering reference for ThisIsMyPC development.

---

## Phase 0: Pre-Registry (Firmware → Bootloader)

Before the registry exists in memory, the system relies entirely on firmware and BCD (Boot Configuration Data). This phase is outside the registry's control surface.

### Signal Path

1. UEFI firmware executes POST, enumerates hardware, locates the EFI System Partition (ESP).
2. Windows Boot Manager (`bootmgfw.efi`) loads from the ESP.
3. Boot Manager reads BCD — a binary hive structurally similar to the registry but stored separately. BCD specifies the OS loader path, boot parameters, timeout, and multi-boot configuration.
4. `winload.efi` takes over: loads `ntoskrnl.exe`, HAL, and the SYSTEM registry hive into memory.

> BCD is not the registry, but it uses the same binary format. You can edit it with `bcdedit.exe`. The registry enters the picture at the very end of this phase when winload maps the SYSTEM hive into memory.

### Key Takeaway for ThisIsMyPC

You cannot control boot-level behavior through the registry alone. BCD modifications require `bcdedit` or direct hive manipulation. However, the moment the SYSTEM hive loads, the registry becomes the primary control plane for everything that follows.

---

## Phase 1: Kernel Initialization (SYSTEM Hive → Drivers)

This is where the registry becomes the operating system's central nervous system. The kernel cannot configure itself without the SYSTEM hive.

### Signal Path

1. `ntoskrnl.exe` initializes the kernel executive, memory manager, and object manager.
2. The kernel reads `HKLM\SYSTEM\CurrentControlSet\Services` to determine the driver load order.
3. Drivers are loaded according to their `Start` value: 0 (boot), 1 (system), 2 (auto), 3 (manual), 4 (disabled).
4. I/O Manager, PnP Manager, and Power Manager initialize, each reading configuration from the SYSTEM hive.
5. Boot-class drivers (Start=0) load first, then system-class (Start=1). The kernel then launches Session Manager (`smss.exe`).

### Driver Start Values

| Value | Type | Description |
|-------|------|-------------|
| `0` | Boot | Loaded by the boot loader before the kernel is fully initialized. Disk, filesystem, and bus drivers. |
| `1` | System | Loaded during kernel init after boot drivers. Core system drivers. |
| `2` | Automatic | Started by the Service Control Manager during normal startup. |
| `3` | Manual | Only started on demand (by a service dependency, PnP, or explicit request). |
| `4` | Disabled | Will not be loaded or started under any circumstance. |

> The `CurrentControlSet` is actually a symlink to one of the `ControlSet00N` keys, determined by the `Select` key. This provides rollback capability — if a driver change causes a boot failure, Last Known Good can point to a previous control set.

---

## Phase 2: Session Manager → Subsystems

`smss.exe` is the first user-mode process. It reads its entire configuration from `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager` and is responsible for setting up the environment that all subsequent processes depend on.

### What smss.exe Configures from Registry

- **Paging file:** Location and size from `Memory Management\PagingFiles`
- **Environment variables:** System-wide env vars from `Environment`
- **Pending file operations:** `PendingFileRenameOperations` — the mechanism behind "reboot to complete" installs
- **Subsystem list:** Which subsystem processes to start (`Required` value)
- **Known DLLs:** Preloaded DLL list from `KnownDLLs` — security-critical, prevents DLL hijacking for listed libraries
- **Session creation:** Starts `csrss.exe` (Win32 subsystem) and `wininit.exe` (session 0) / `winlogon.exe` (user session)

> smss.exe is the registry's most obedient consumer. It does essentially nothing that isn't dictated by Session Manager keys. This makes it a powerful but dangerous control point — misconfiguration here can render the system unbootable.

---

## Phase 3: Services and Logon

Two parallel paths diverge from smss.exe: the service infrastructure (session 0) and the interactive logon (user session).

### Service Control Manager

`wininit.exe` starts `services.exe` (the SCM), which enumerates every subkey under `HKLM\SYSTEM\CurrentControlSet\Services`. For each entry marked with Start=2 (Automatic), the SCM starts the service, respecting dependency chains defined in `DependOnService` and `DependOnGroup` values.

This is where the bulk of the OS comes alive: networking (tcpip, dhcp, dnscache), security (SamSs, lsass), RPC infrastructure, audio, event logging, Windows Update, and hundreds of other services are all defined as registry service entries.

### Winlogon and Shell

`winlogon.exe` reads `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` for critical configuration: the `Shell` value (defaults to `explorer.exe`), credential provider registration, logon scripts, and the Secure Attention Sequence (Ctrl+Alt+Del) behavior.

> Changing the `Shell` value in Winlogon is one of the most impactful single-key modifications possible. It controls what runs as the user's desktop. Kiosk modes, custom shells, and malware persistence all leverage this key.

---

## Phase 4: User Environment

After authentication, the per-user signal flow activates.

### Profile Load

1. User authenticates via credential provider → LSASS validates against SAM or AD.
2. The user's profile hive (`NTUSER.DAT`) is mounted as `HKCU`.
3. `UsrClass.dat` is mounted as `HKCU\Software\Classes` (per-user file associations and COM registrations).
4. `explorer.exe` launches, reading shell configuration from both HKLM and HKCU.

### Startup Execution Order

Once the shell is live, startup entries fire. The order matters and is consistent:

| # | Location | Scope |
|---|----------|-------|
| 1 | `HKLM\...\Run` | Machine-wide, every logon |
| 2 | `HKCU\...\Run` | Per-user, every logon |
| 3 | `HKLM\...\RunOnce` | Machine-wide, one time then deleted |
| 4 | `HKCU\...\RunOnce` | Per-user, one time then deleted |
| 5 | Startup folder (All Users) | Machine-wide shortcuts |
| 6 | Startup folder (User) | Per-user shortcuts |
| 7 | Scheduled Tasks (at logon) | Task Scheduler triggers |

> The Run key paths are `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` and the HKCU equivalent. These are the most commonly targeted persistence mechanisms for both legitimate software and malware.

---

## Phase 5: Runtime — The Registry as a Live Bus

Post-boot, the registry is not a static config file. It functions as a live, event-driven, shared-state database that the entire OS reads from and writes to continuously.

### Active Consumers at Runtime

- **Group Policy engine:** Periodically writes policy results to registry keys under both HKLM and HKCU. Many GPO settings are purely registry writes.
- **Security Reference Monitor:** Reads security policy, audit settings, and privilege assignments from the registry.
- **PnP Manager:** Writes hardware enumeration data to `HKLM\SYSTEM\CurrentControlSet\Enum` whenever devices are connected or disconnected.
- **Applications:** Read settings, subscribe to change notifications via `RegNotifyChangeKeyValue`. This makes the registry an IPC mechanism.
- **Shell (Explorer):** Continuously watches HKCU for preference changes, file association updates, and context menu modifications.
- **Windows Update:** Stores update state, pending operations, and configuration in multiple registry locations.

### Change Notification Model

The `RegNotifyChangeKeyValue` API allows any process to watch a registry key for changes without polling. This transforms the registry from a simple config store into an event bus. When a value changes, all watchers are notified asynchronously. This is how Explorer knows to update the shell when you change a preference, and how Group Policy propagates settings without requiring a reboot for most policies.

---

## What the Registry Does NOT Control

For completeness, these subsystems maintain their own state outside the registry. Some read from the registry but are not primarily governed by it.

| Subsystem | State Store | Registry Interaction |
|-----------|-------------|---------------------|
| Boot Config (BCD) | Binary hive on ESP | None — separate store |
| Certificate Store | Protected storage files | Reads some policy keys |
| Task Scheduler | XML files in System32\Tasks | Minimal — service config only |
| WMI Repository | OBJECTS.DATA in Repository/ | Independent data store |
| Firewall Rules | WFP engine + own DB | Some policy keys |
| Group Policy Templates | ADMX/ADML files | Writes results TO registry |
| App Configs | AppData (JSON/XML/INI) | Completely independent |
| NTFS Permissions | MFT / SD stream | None — filesystem-level |
| Credential Manager | Protected storage | Configuration keys only |

---

## Complete Signal Flow Summary

The full boot-to-desktop signal flow, showing where the registry acts as the control plane at each transition.

| From | → | To | Controlled By |
|------|---|-----|---------------|
| UEFI | → | Boot Manager | Firmware (not registry) |
| Boot Manager | → | winload.efi | BCD hive (not registry) |
| winload | → | ntoskrnl | **SYSTEM hive loaded — REGISTRY ENTERS** |
| ntoskrnl | → | Drivers | `Services\*\Start` values (registry) |
| Kernel | → | smss.exe | Session Manager keys (registry) |
| smss.exe | → | csrss.exe | `SubSystems\Required` (registry) |
| smss.exe | → | wininit.exe | Session 0 init (registry) |
| wininit | → | services.exe | All Services keys (registry) |
| smss.exe | → | winlogon.exe | Winlogon keys (registry) |
| winlogon | → | explorer.exe | `Shell` value (registry) |
| Auth | → | HKCU mount | NTUSER.DAT → HKCU (registry) |
| Explorer | → | Desktop | Run keys + shell config (registry) |

---

## Implications for ThisIsMyPC

Understanding this signal flow directly informs the architecture of ThisIsMyPC as a control surface over Windows:

- **Live-reloadable settings** (most HKCU shell preferences, many HKLM\SOFTWARE values) can be changed and take effect immediately or after an Explorer restart. These are your fast-path controls.
- **Reboot-required settings** (driver Start values, Session Manager config, some security policies) take effect only on next boot. Flag these clearly in the UI.
- **Service manipulation** can be done live via the SCM API (`sc.exe` / Win32 service APIs) but the registry is the persistence layer. Change the registry to survive reboots, use the SCM for immediate effect.
- **The non-registry subsystems** (BCD, Task Scheduler, Firewall, Certificates) each need their own API surface. Don't try to control them through the registry — use their native APIs.
- **`RegNotifyChangeKeyValue`** is your friend for reactive UI. Subscribe to keys you care about and update the control surface in real time when other processes (Group Policy, installers, etc.) modify them.

---

> **Future sections:** HKLM\SOFTWARE hive deep dive, per-user vs. per-machine policy conflicts, registry virtualization (WoW64, UAC), and transactional registry (KTM).
