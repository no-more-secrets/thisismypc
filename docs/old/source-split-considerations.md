# ThisIsMyPC — Source Split Considerations

**Date:** March 8, 2026
**Purpose:** Define the public/private repository boundary for GPLv2 compliance and security architecture protection.

---

## Guiding Principles

1. All code derived from or linking against GPLv2 dependencies (OpenRGB, PawnIO) must be public under GPLv2.
2. The module contract, core library, GUI, and all user-facing modules are public — this is where community trust is earned through transparency.
3. Security-critical boundary enforcement code (IPC authentication, IOCTL validation, bytecode signing, update verification) is proprietary and private. This code is authored independently, is not derived from any GPL source, and communicates with GPL components exclusively over IPC channels.
4. The boundary between public and private is a process/IPC boundary, not a linking boundary. No shared source files, no static linking, no header inclusion across the boundary.

---

## Public Repository (GPLv2)

### Application Shell & Core
- `ThisIsMyPC.App` — Avalonia host, UI shell, navigation, module loading, DI setup
- `ThisIsMyPC.Core` — IModule contract, ChangeDescriptor, PendingChangesService, OperationResult, SettingsService, ChangeHistoryService, SQLite persistence

### Interop Layers
- `ThisIsMyPC.Interop.Win32` — CsWin32 P/Invoke wrappers (registry, SCM, powrprof, DXVA2, SetupAPI)
- `ThisIsMyPC.Interop.Com` — COM interop (ITaskService, shell extension handlers)
- `ThisIsMyPC.Interop.Wmi` — WMI queries (system info, ASUS ATKACPI)

### Modules (All First-Party)
- `ThisIsMyPC.Modules.Shell` — Explorer + Context Menus
- `ThisIsMyPC.Modules.Startup` — Startup & Services
- `ThisIsMyPC.Modules.Power` — Power Plans
- All future first-party modules following the same pattern

### Native / GPL Dependencies
- OpenRGB fork module (GPLv2, derived work)
- PawnIO driver integration module (GPLv2, communicates with GPLv2 driver)

### Tests
- All unit and integration test projects for the above

### Documentation
- Architecture doc, module development guides, contributor guidelines

---

## Private Repository (Proprietary)

### Session 0 Security Service
- Service binary and entry point
- Named pipe server implementation with authentication protocol
- SDDL ACL configuration and connection validation logic
- Mutual authentication / cryptographic handshake implementation
- Client identity verification routines

### IOCTL Security Layer
- IOCTL dispatch validation between Session 0 service and PawnIO driver
- Input sanitization and bounds checking routines
- Bytecode payload signing and verification logic
- Whitelisted bytecode module enforcement
- Caller integrity verification

### State Enforcement Security
- CmRegisterCallbackEx protected key list management
- Cryptographic verification of callback target modifications
- FltRegisterFilter configuration and access control
- Anti-tampering logic for service binaries and registry entries

### Update Integrity
- Velopack update verification implementation
- Cryptographic checksum validation logic
- Certificate pinning and TLS verification configuration
- Release signing procedures and key management

### Anti-Spoofing
- Owner Mode activation verification UI logic
- Anti-DLL-sideloading hardening
- Runtime integrity checks

---

## Boundary Enforcement Rules

1. **No GPL source in the private repo.** Ever. Not even copied snippets.
2. **No private source in the public repo.** The public solution file excludes private projects entirely.
3. **Communication is IPC only.** Named pipes between GUI and Session 0 service. IOCTLs between service and driver. No shared memory, no direct function calls, no linking.
4. **Separate build pipelines.** Public repo builds the GPL application. Private repo builds the security service. Final distributable combines both without leaking source from either side.
5. **Interface contracts are public.** The named pipe message format and IOCTL codes can be documented in the public repo as interface specifications. The *implementation* of authentication and validation on those interfaces stays private.
6. **The driver is public.** PawnIO is GPLv2 and its source is public. The driver's security comes from constrained interfaces, not obscurity. How the service authenticates to the driver is private.

---

## Legal Notes

- GPLv2 copyleft applies to derivative works. The private security service is an independent program communicating over IPC — not a derivative work of the GPL codebase.
- This position is consistent with established precedent (Linux kernel proprietary module boundary, FSF guidance on program aggregation vs. derivative works).
- Formal legal review recommended before stable release with proprietary components.
