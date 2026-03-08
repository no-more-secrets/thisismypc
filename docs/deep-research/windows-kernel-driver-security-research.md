---
author: Gemini 3.1 Pro (Deep Research mode)
date: 2026-03-08
---

# Windows 11 Kernel Architecture and Ring 0 Driver Landscape

## Introduction to the Modern Windows Kernel Trust Model

The Windows 11 kernel architecture represents a fundamental paradigm shift from legacy monolithic trust models to a highly compartmentalized, hypervisor-backed security environment. Historically, Ring 0 (kernel mode) was an absolute trust boundary; any code executing within it possessed unmitigated access to system memory, hardware peripherals, and process states. However, the proliferation of sophisticated rootkits, Bring Your Own Vulnerable Driver (BYOVD) attacks, and aggressive third-party kernel mechanisms has forced a structural evolution within Microsoft's operating system design.[^1]

Today, Windows 11 utilizes Virtualization-Based Security (VBS) and Hypervisor-Protected Code Integrity (HVCI) to dynamically restrict even Ring 0 code, effectively placing the hypervisor (Ring -1) as the ultimate arbiter of execution privileges.[^2] This architectural pivot dictates that kernel drivers can no longer assume unfettered access to hardware registers or executable memory allocation.

Consequently, third-party developers -- spanning open-source hardware control utilities to enterprise Endpoint Detection and Response (EDR) sensors -- must navigate an increasingly complex labyrinth of cryptographic signing requirements, rigid Inter-Process Communication (IPC) models, and strictly enforced hardware abstraction layers.

This comprehensive report provides an exhaustive architectural analysis of the Windows 11 kernel driver landscape. It systematically deconstructs driver signing prerequisites, Microsoft's native enforcement drivers, hardware control abstraction, inter-process communication security, anti-cheat and EDR mechanics, and the underlying Hardware Abstraction Layer (HAL). By synthesizing recent vulnerability patterns, including the catastrophic July 2024 CrowdStrike incident, this analysis culminates in a secure architectural blueprint for open-source hardware control drivers operating in modern, restrictive kernel environments.

## The Driver Loading and Cryptographic Signing Architecture

The driver loading mechanism in Windows 11 enforces cryptographic validation at multiple stages of the boot and execution sequence. To execute code in the Windows kernel, developers must navigate a rigorous certification and signing architecture governed by the Microsoft Hardware Dev Center, fundamentally shifting the burden of trust from the local machine to Microsoft's centralized public key infrastructure.

### Extended Validation (EV) Certificate Obligations

Since the deprecation of cross-signing certificates in 2021, Microsoft mandates that all production kernel-mode drivers be signed through its centralized dashboard.[^4] To submit any driver payload for either WHQL certification or Attestation signing, an organization must possess an Extended Validation (EV) Code Signing Certificate associated with its Partner Center account.[^5] These certificates undergo rigorous identity verification processes and must be purchased from authorized Certificate Authorities (CAs) such as Certum, DigiCert, GlobalSign, IdenTrust, Sectigo, or SSL.com.[^5]

The technical specifications for these submissions are stringent. All certificates must utilize the SHA-256 algorithm, and submissions must be signed using the `/fd sha256` SignTool command-line switch.[^5] Furthermore, the registered EV certificate must be valid at the exact time of the submission.[^5] This mechanism ensures strict organizational identity verification, theoretically raising the barrier to entry for malicious actors seeking to distribute weaponized kernel modules, as acquiring an EV certificate requires establishing a verifiable corporate entity.

### Attestation Signing versus WHQL Certification

The Microsoft Hardware Developer Center offers two primary pathways for kernel driver signing: Attestation Signing and Windows Hardware Quality Labs (WHQL) Certification. These two pathways serve vastly different distribution models and impose different engineering burdens on the developer.

| Architectural Feature | Attestation Signing | WHQL Certification |
|---|---|---|
| **Testing Prerequisites** | Zero Hardware Lab Kit (HLK) tests required.[^5] | Must pass rigorous, pre-defined HLK testing.[^7] |
| **Distribution Scope** | Cannot be published to Windows Update for retail audiences.[^5] | Fully eligible for automated Windows Update distribution.[^5] |
| **OS Compatibility Limit** | Windows 10 Desktop and Windows 11 architectures only.[^5] | Windows 10, Windows 11, and Windows Server environments.[^4] |
| **Excluded Binary Types** | ELAM (Early Launch Antimalware) and Windows Hello PE binaries.[^5] | Supports all binary types upon successful test completion.[^5] |
| **Primary Use Case** | Rapid deployment, internal testing, and open-source utilities (e.g., PawnIO).[^5] | Retail hardware integration, enterprise EDR solutions, and OEM drivers.[^8] |

Attestation signing provides a highly streamlined approach for utilities like the open-source PawnIO driver.[^5] It requires the driver to be packaged in a specific folder structure (under 40 characters in length, containing no special characters, and avoiding UNC file share paths) and submitted via the Partner Center.[^5] Microsoft signs the payload, indicating that the driver is trusted by the Windows operating system, though it explicitly does not guarantee compatibility or stability, as the binary has bypassed HLK Studio verification.[^5]

Conversely, WHQL certification requires the generation of `.hlkx` test logs via the Windows HLK Studio, proving that the driver handles power transitions, memory management, and concurrent I/O operations without causing system instability.[^4]

For an open-source driver like PawnIO to be legally and technically loaded on a consumer Windows 11 system without forcing the user to disable Secure Boot or enable Test Mode via the `bcdedit` utility, it must attain at least an Attestation signature.[^4] Test signing is strictly limited to development environments where the end-user has intentionally compromised their boot chain security.[^5]

### Secure Boot and Driver Signature Enforcement (DSE)

Secure Boot anchors the system's foundational trust by validating the bootloader's cryptographic signature against the Unified Extensible Firmware Interface (UEFI) databases before the operating system is even invoked.[^5] Once the Windows kernel is initialized, Driver Signature Enforcement (DSE) assumes control of the trust boundary. DSE rigorously verifies that every `.sys` file mapped into kernel memory carries a valid Microsoft signature.[^6] If a driver lacks this signature, or if the signature has been explicitly revoked via the Microsoft Vulnerable Driver Blocklist (as seen with legacy drivers like WinRing0), DSE will immediately block the load operation, returning an invalid image hash error and preventing the code from executing in Ring 0.[^1]

## Native Microsoft Kernel-Level Enforcement Drivers

Microsoft actively deploys native kernel filter drivers to protect critical system configurations from unauthorized modification. These drivers represent a philosophical shift where the operating system actively resists configuration changes initiated by the user or third-party applications, even against processes running with administrative or SYSTEM privileges. By intercepting specific system calls, these drivers rigidly enforce Microsoft's preferred state.

### The UserChoice Protection Driver (ucpd.sys)

Introduced stealthily in early 2024 via Windows Updates (KB5034765), `ucpd.sys` is a velocity-gated filter driver explicitly designed to block unauthorized changes to default application associations.[^12] It specifically targets the UserChoice registry keys associated with web protocols (`http`, `https`) and document extensions (`.pdf`), effectively neutralizing utilities that attempt to bypass the Windows 11 Settings app to assign alternate default browsers.[^12]

Architecturally, `ucpd.sys` operates using a strict, bipartite logic model based on process identity validation. The driver's `IsInDenyList` routine contains hardcoded blocks against common administrative execution vectors, explicitly denying binaries such as `reg.exe`, `powershell.exe`, `cmd.exe`, `rundll32.exe`, and `WmiPrvSE.exe` from modifying the protected hives.[^14] Conversely, its `IsMicrosoftSignedFile` routine acts as a whitelist, permitting modifications only if the calling binary is cryptographically signed by Microsoft.[^14] This permits the native Windows Settings application to alter defaults while simultaneously blocking third-party browsers or administrative scripts.

The persistence architecture of `ucpd.sys` is particularly aggressive. The driver is marked as `NOT_STOPPABLE`, meaning it lacks an unload routine and cannot be detached via the Filter Manager (`fltmc`) during runtime.[^12] Furthermore, its persistence is managed by an auxiliary user-mode executable (`UCPDMgr.exe`) invoked by a scheduled task located at `\Microsoft\Windows\AppxDeploymentClient\UCPD velocity`.[^12] This auxiliary task ensures the driver is continually re-enabled upon reboot, even if a user manually disables the service in the registry by setting the `Start` value to `4` (Disabled).[^12]

### Windows Defender Filter Driver (wdfilter.sys)

The `wdfilter.sys` binary acts as the primary kernel enforcement arm for Microsoft Defender. It heavily utilizes the `ObRegisterCallbacks` API to protect its own registry hives and processes from tampering or termination.[^16]

The initialization phase begins in the `MpObInitialize` function, which dynamically resolves the addresses for object registration and applies callbacks for `PsProcessType` and `ExDesktopObjectType`.[^17] When a user-mode application attempts to open a handle to a protected process (such as the core Defender engine, `MsMpEng.exe`), `wdfilter.sys` intercepts the `OB_PRE_OPERATION_INFORMATION` packet.[^17]

The callback routine (`MpObHandleOpenProcessCallback`) evaluates the requested access rights against the caller's process context. If the caller requests sensitive access rights such as `PROCESS_VM_WRITE`, `PROCESS_VM_OPERATION`, or `PROCESS_CREATE_THREAD`, the driver evaluates whether code injection is permitted via the `MpAllowCodeInjection` function.[^17] Injection is strictly limited to processes flagged internally as `ExcludedProcess` (`0x1`), `MpServiceSidProcess` (`0x10`), or `FriendlyProcess` (`0x20`).[^17]

For unprivileged processes, the driver utilizes Host Intrusion Prevention System (HIPS) rules (such as `AllowCodeInjectionHIPSRule` `0x8000` and `QuerySuspendResumeHIPSRule` `0x800000`) to determine intent.[^17] If the caller lacks authorization, `wdfilter.sys` silently strips these sensitive access rights from the `DesiredAccess` mask before the handle is returned to the Object Manager.[^17] This effectively neutralizes attempts to inject code or terminate Defender, while its concurrent registry filtering callbacks block modifications to the `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender` keys, preventing malware from disabling real-time protection.[^16]

### The Early Launch Antimalware (ELAM) Framework

The ELAM framework represents the earliest phase of the third-party enforcement architecture. ELAM allows certified security vendors to load their initialization drivers immediately after the core Windows boot-start drivers, but crucially, before any third-party software or generic hardware drivers.[^5]

ELAM drivers are granted a special PE trust level that permits them to subscribe to system boot callbacks. They evaluate subsequent boot drivers and classify them into categories: known good, known bad, or unknown. If an ELAM driver classifies a subsequent module as malicious, the Windows kernel intervenes and halts the loading of that specific module, neutralizing sophisticated rootkits before they can execute their `DriverEntry` routines and establish persistence.[^5]

## Hardware Control Drivers and the Driver Model

Third-party hardware utilities -- ranging from RGB controllers like OpenRGB to thermal management software like FanControl and vendor-specific suites (iCUE, Armoury Crate, Synapse) -- require direct communication with peripheral hardware.[^10] This communication is constrained by the Windows Driver Model (WDM) and the more modern Windows Driver Framework (WDF).[^19]

### The Legacy Paradigm: The WinRing0 Vulnerability

Historically, hardware control utilities relied heavily on the open-source `WinRing0.sys` driver to achieve their functionality.[^10] WinRing0 provided user-mode applications with arbitrary, unfettered access to CPU Model-Specific Registers (MSRs), physical memory mapping, and direct IN/OUT port execution.[^10] While this architecture allowed software to seamlessly read motherboard embedded controllers (ECs) and precisely manipulate System Management Bus (SMBus)/I2C interfaces for RGB lighting and fan curves, it completely violated modern OS security paradigms.[^22]

Because WinRing0 did not validate the calling process or constrain the memory addresses being mapped, any unprivileged malware could leverage it to achieve arbitrary read and write access to the kernel.[^22] This architectural flaw allowed threat actors to bypass user-mode restrictions entirely. Consequently, Microsoft Defender began explicitly flagging WinRing0 as a vulnerability (`HackTool:Win32/Winring0` / CVE-2020-14979), categorizing it as a known exploitation vector.[^22] This action effectively broke widespread utilities and forced the open-source community to pivot toward more secure abstraction models.[^23]

### The Modern Abstraction: PawnIO and ACPI Invocation

To replace the deprecated WinRing0 model, developers architected specialized WDF drivers like PawnIO.[^10] PawnIO operates as a scriptable universal kernel driver utilizing the custom PawnPP bytecode interpreter.[^23] Instead of exposing raw physical memory mapping and arbitrary port access to user-space, PawnIO limits execution to predefined, safe bytecode modules.[^23] This interpreter model establishes a protective barrier, allowing the driver to perform safety checks on the requested hardware operations before executing them in Ring 0.

However, direct hardware manipulation is increasingly scrutinized by the kernel's virtualization boundaries. Modern hardware control relies heavily on Advanced Configuration and Power Interface (ACPI) method invocation to ensure compatibility and safety.[^20] Kernel drivers interface with the ACPI subsystem using the `IOCTL_ACPI_EVAL_METHOD` control code.[^20] To evaluate a specific hardware method (e.g., querying a thermal zone or adjusting a fan PWM duty cycle), the driver constructs an `ACPI_EVAL_INPUT_BUFFER_SIMPLE_INTEGER` structure, populating the `MethodNameAsUlong` member with the targeted ACPI node name.[^20] The driver calls `IoBuildDeviceIoControlRequest` to package the input and output buffers, then sends the I/O Request Packet (IRP) down the device stack via `IoCallDriver`.[^20]

The Windows ACPI driver (`Acpi.sys`) receives this IRP, evaluates the control method safely within the constraints of the ACPI namespace, and returns the output.[^20] This approach adheres strictly to the WDF/WDM standard and avoids the dangerous, arbitrary I/O port polling that triggers Hypervisor-Protected Code Integrity (HVCI) violations.

## Inter-Process Communication (IPC) Mechanisms and Security Models

The architecture of userspace-to-kernel communication dictates the fundamental attack surface of a driver. Windows 11 offers several Inter-Process Communication (IPC) mechanisms, each characterized by distinct security considerations, access control enforcement, and historical abuse patterns.

### IOCTLs (DeviceIoControl) Security Tradeoffs

The most prevalent IPC mechanism is the `DeviceIoControl` API, which routes IRPs from a user-mode process to the driver's `IRP_MJ_DEVICE_CONTROL` dispatch routine.[^30] The security model of IOCTLs hinges heavily on the Access Control List (ACL) applied to the driver's device object during initialization.[^31]

Secure drivers invoke the `IoCreateDeviceSecure` routine, passing a Security Descriptor Definition Language (SDDL) string that explicitly restricts handle creation to the SYSTEM account or the local Administrators group.[^31] Furthermore, developers must set the `FILE_DEVICE_SECURE_OPEN` characteristic; if this is omitted, a malicious user-mode process could bypass the device ACL by opening a handle to a trailing namespace path.[^31]

Poorly secured IOCTLs are the primary vector for Local Privilege Escalation (LPE) and BYOVD attacks. If a driver defines an IOCTL with `FILE_ANY_ACCESS` and utilizes `METHOD_BUFFERED` without strictly validating the input buffer's length and content boundaries, an attacker can supply malformed data to trigger buffer overflows, arbitrary memory writes, or unauthorized system actions.[^31] The Microsoft security model explicitly dictates that any data crossing the trust boundary from user-space to kernel-space must be treated as hostile and subjected to rigorous bounds checking.[^31]

### Filter Manager Communication Ports

For file system minifilters and EDR sensors, Microsoft provides a dedicated, highly structured communication port architecture.[^34] A driver invokes `FltCreateCommunicationPort` to instantiate a named port (e.g., `\MyFilterPort`) within the object manager.[^34] User-mode services establish a connection to this port using `FilterConnectCommunicationPort`, allowing for bidirectional, asynchronous message passing via the `FltSendMessage` and `FltReceiveMessage` APIs.[^34]

This architecture is inherently more secure than raw IOCTLs. The Filter Manager automatically handles buffer probing, context tracking, and connection teardown securely, minimizing the risk of kernel pool corruption.[^34] Access control is enforced by a security descriptor passed during port creation, ensuring only authorized services can initiate a connection.[^34]

### The Challenge of Caller Validation

A persistent architectural challenge in Windows kernel development is reliably restricting driver communication to a specific, authorized user-mode process.[^32] While a driver can retrieve the calling process's Process ID (PID) via `PsGetCurrentProcessId` and inspect its security context using `SeCaptureSubjectContext`, validating the true origin and integrity of the caller is fraught with difficulty.[^32]

PIDs can be reused or spoofed by rapid process termination and creation. Relying on the cryptographic signature of the calling binary is vulnerable to Time-of-Check to Time-of-Use (TOCTOU) race conditions, or sophisticated DLL hijacking within the context of the otherwise trusted process.[^32]

Consequently, the most robust security models eschew PID validation in favor of strict Administrator/SYSTEM ACL enforcement combined with mandatory, exhaustive input sanitization.[^31]

## Architecture of Anti-Cheat and EDR Solutions in Ring 0

Anti-cheat systems and enterprise EDR products require the deepest level of system access to detect and intercept anomalous behaviors, memory injection, and sophisticated malware. Their architectures demonstrate the zenith of Windows kernel manipulation, but they also brutally highlight the catastrophic risks associated with Ring 0 failures.

### Riot Vanguard: Establishing a Pre-Boot Perimeter

Riot Vanguard represents one of the most aggressive anti-cheat architectures deployed on consumer systems.[^37] Operating as a boot-start kernel driver (`vgk.sys`), Vanguard establishes a defensive "perimeter" before any user-mode cheats or secondary drivers can initialize.[^39]

Vanguard's tamper protection acts defensively by enforcing strict system prerequisites rather than merely scanning for signatures. It issues `VAN:Restriction` codes if it detects that virtualization-based security features, such as TPM 2.0, Secure Boot, or the Input-Output Memory Management Unit (IOMMU), are disabled or misconfigured.[^39] By enforcing IOMMU initialization early in the UEFI boot sequence, Vanguard explicitly neutralizes hardware-based Direct Memory Access (DMA) cheats (e.g., PCIe screamers inserted into motherboard slots) from reading the game client's system memory.[^39]

Furthermore, Vanguard extensively utilizes `ObRegisterCallbacks` to strip handle permissions from unauthorized processes attempting to attach debuggers or read the memory space of the protected game client.[^37]

### BattlEye: Polymorphism and Memory Anomaly Scanning

BattlEye utilizes a distributed model consisting of a persistent system service (`BEService.exe`) and a dynamically loaded kernel driver (`BEDaisy.sys`).[^42] To evade static analysis and reverse-engineering by cheat developers, BattlEye employs dynamic payload delivery.[^43] The backend infrastructure (`BEServer`) streams encrypted shellcode directly to `BEService`, which then maps it to the kernel driver.[^43] This polymorphic approach ensures the detection routines constantly mutate.[^43]

Once active, the driver enumerates the virtual address space of the game process using APIs such as `NtQueryVirtualMemory`.[^43] It aggressively scans for memory pages marked as Executable/Read/Write (RWX) that do not correspond to a legitimately loaded and cryptographically signed module.[^43] Any such anomaly is flagged as a potential manual-mapped payload, resulting in a swift ban.[^43]

### CrowdStrike Falcon and the July 2024 Architectural Failure

EDR solutions like CrowdStrike Falcon (`CSagent.sys`) operate as file system minifilters and object callback providers to gain comprehensive system visibility across the enterprise.[^44] However, the global IT outage on July 19, 2024, exposed the severe architectural fragility of running complex parsing logic directly within Ring 0.[^44]

The root cause of the incident traced back to a "Rapid Response Content" update delivered via "Channel File 291".[^45] This channel file contained newly deployed configuration templates designed to evaluate named pipes (IPC) for malicious behavior.[^45] The updated template supplied 21 input parameter fields; however, the kernel-mode content interpreter compiled within `CSagent.sys` was rigidly hardcoded to expect exactly 20 values.[^45] When the Windows kernel generated an IPC notification, the driver's content interpreter attempted to read the 21st parameter, resulting in an out-of-bounds memory read.[^45]

Because this memory violation occurred inside a highly privileged, boot-start kernel driver, the resulting exception (`PAGE_FAULT_IN_NONPAGED_AREA`) went unhandled, triggering an immediate Blue Screen of Death (BSOD).[^44] Furthermore, because the driver was designated as essential for the boot sequence, the affected systems entered an infinite boot loop, requiring laborious manual remediation via Safe Mode or the Windows Recovery Environment (WinRE) to delete the offending `.sys` file.[^44]

The architectural lesson derived from the CrowdStrike incident is absolute: complex parsing of dynamically fetched, unsigned, or unverified data structures should never occur directly within the kernel.[^47] Robust security architecture dictates that parsing logic must reside in isolated user-mode services, passing only thoroughly sanitized, strictly bounded binary structs to the kernel via heavily validated IPC channels.[^49]

## The Hardware Abstraction Layer and Hardware Access Protections

The pursuit of direct hardware access by tools ranging from OpenRGB to kernel-level anti-cheats is increasingly clashing with Microsoft's modern security boundary: the hypervisor.[^2]

### Hypervisor-Protected Code Integrity (HVCI) and VBS

HVCI (often referred to as Memory Integrity) leverages Virtualization-Based Security (VBS) to fundamentally alter the hierarchy of trust within Windows 11.[^2] Under the VBS architecture, the primary Windows OS runs in Virtual Trust Level 0 (VTL 0), while a Secure Kernel operates in a highly isolated environment known as VTL 1.[^50]

HVCI utilizes hardware-assisted virtualization features, specifically Second Level Address Translation (SLAT) and Extended Page Tables (EPT), to strip the primary OS kernel of its historical ability to mark memory pages as executable.[^3] When a driver in VTL 0 attempts to allocate executable memory, the request is intercepted by VTL 1.[^50] The Secure Kernel verifies the digital signature of the code against the system's code integrity policy; only if the signature is valid does it modify the EPT to grant the +X (Execute) permission.[^50] This architecture enforces strict Write-XOR-Execute (W^X) protections.[^3]

Drivers that attempt arbitrary memory mapping, rely on RWX sections, or attempt to clear the `CR4.SMEP` (Supervisor Mode Execution Prevention) register to bypass restrictions will trigger immediate hypervisor interceptions and system halts.[^51] Older anti-cheat solutions, such as certain builds of Easy Anti-Cheat, have historically caused friction by actively conflicting with Kernel-Mode Hardware-Enforced Stack Protection and HVCI, forcing users into difficult compromises between gaming functionality and baseline OS security.[^53]

### I/O Port and MSR Restrictions

Legacy drivers like WinRing0 used assembly instructions like `IN`/`OUT` and `RDMSR`/`WRMSR` to directly interact with hardware registers.[^23] While the Root Partition (VTL 0) still retains access to a significant portion of physical memory and I/O ports, HVCI imposes strict guardrails.[^51] Certain Model-Specific Registers (such as the SYSENTER MSRs) are entirely restricted by the hypervisor to prevent rootkits from hijacking system call handlers.[^51]

Furthermore, aggressive polling of undocumented embedded controller ports can violate hardware isolation boundaries. When a driver attempts an incompatible I/O operation, Windows 11 will often silently disable HVCI upon reboot to preserve system stability, logging a Kernel-Boot failure regarding the inability to update Secure Boot Advanced Targeting (SBAT) firmware values.[^55]

Legitimate hardware control utilities must therefore transition to WDF-compliant ACPI calls or user-mode APIs (like SpbCx for I2C) to ensure they do not inadvertently trigger security rollbacks.[^55]

### Kernel DMA Protection

To defend against drive-by physical attacks via Thunderbolt, USB4, or PCIe slots, Windows 11 employs Kernel DMA Protection.[^2] This technology relies on the Input-Output Memory Management Unit (IOMMU) to block unauthorized, hot-plugged peripherals from performing Direct Memory Access until the user authenticates and the OS explicitly grants permission.[^2]

Hardware control drivers attempting to map physical DMA buffers for high-speed device interaction must use the official DMA abstraction APIs (such as `AllocateCommonBuffer`) rather than manually constructing scatter-gather lists in raw physical memory, ensuring compatibility with the IOMMU guardrails.[^2]

## Service Architecture and Process Persistence

Persistent background processes in Windows 11 are managed by the Service Control Manager (SCM). The architecture mandates strict isolation to prevent unprivileged users from manipulating high-privilege operations, dictating how an open-source tool must be designed to persist safely.

### Session 0 Isolation Mechanics

Prior to the release of Windows Vista, services and user applications shared the same interactive desktop environment (Session 0).[^57] This shared environment led to catastrophic privilege escalation vulnerabilities known as "shatter attacks," where malicious user-mode applications sent crafted window messages to highly privileged services, tricking them into executing arbitrary code.[^58]

To remediate this, modern Windows enforces strict Session 0 Isolation.[^57] The SCM, all native system services, and boot-start drivers run exclusively in Session 0.[^57] When the first interactive user logs in, they are placed in Session 1.[^57] Processes operating in Session 0 are completely detached from the interactive desktop; they cannot render GUI elements, intercept user keystrokes, or share named synchronization objects (unless explicitly prefixed with `Global\`).[^58]

For a hardware control tool to persist across reboots and user logoffs, its core backend must be installed as a service running under the `NT AUTHORITY\SYSTEM` account in Session 0.[^61] To display a configuration user interface, a separate, low-privilege user-mode client must run in Session 1.[^59] This client communicates with the Session 0 service via secure RPC or named pipes protected by strict ACLs, ensuring that UI interactions cannot be weaponized to compromise the background service.[^59]

### Service Protection Mechanisms

Advanced threat actors routinely target the SCM to establish persistence, often configuring malicious binaries in the `HKLM\System\CurrentControlSet\Services` hive or replacing legitimate service DLLs.[^36] To defend a legitimate hardware control service against tampering, developers must utilize several defense-in-depth mechanisms:

1. **Binary Protection:** The service executable itself must reside in a highly secure directory (e.g., `C:\Program Files\`) where standard, unprivileged users lack write permissions.[^36]
2. **Registry Protection:** The service's registry keys must be secured. While Windows Defender enforces this internally using its kernel filter, standard third-party services rely on the inherent ACLs of the HKLM hive, which strictly requires Administrator privileges to modify.[^63]
3. **Process Protection Light (PPL):** High-security EDR services run as PPL processes, a kernel-enforced flag that prevents even Administrator-level processes from inspecting their memory or terminating them.[^65] However, achieving PPL status requires specialized Microsoft Early Launch Antimalware certificates that are not readily available to open-source developers, forcing them to rely heavily on standard ACLs and correct directory permissions.[^65]

## State Enforcement via Callbacks and Filtering Frameworks

Microsoft relies extensively on kernel callbacks to enforce OS integrity. However, this exact same architectural framework can theoretically be leveraged by third-party drivers to enforce a user's preferred state, essentially turning the enforcement architecture against Microsoft's own automated updates or feature changes.

### Registry Filtering (CmRegisterCallbackEx)

The Windows Configuration Manager allows kernel drivers to intercept registry operations via the `CmRegisterCallbackEx` API.[^67] When a thread attempts to read, write, or enumerate a registry key, the registered callback is triggered synchronously, immediately before the operation commits to the hive.[^68] The callback receives a `REG_XXX_KEY_INFORMATION` structure, allowing the filter driver to deeply inspect the desired access, modify the data in-flight, or block the operation entirely by returning `STATUS_ACCESS_DENIED`.[^68]

A third-party state-enforcement driver could weaponize this mechanism to rigidly protect user preferences. For example, to prevent Windows Update from automatically replacing functioning GPU or hardware control drivers with inferior generic versions, a user could deploy a registry filter driver that monitors writes to `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`.[^69] By forcibly enforcing the `ExcludeWUDriversInQualityUpdate = 1` key and actively blocking the `UsoClient.exe` (Update Session Orchestrator) from reverting it, the driver would lock the system into the user's preferred driver state, preventing automated overrides.[^70]

### File System Minifilters (FltRegisterFilter)

Similarly, the Filter Manager framework allows drivers to seamlessly intercept file system I/O operations.[^71] By invoking `FltRegisterFilter`, a driver registers pre-operation and post-operation callbacks for file creation, reading, and writing.[^71]

If an open-source utility needs to guarantee its critical configuration files or dependent DLLs are never overwritten or deleted by an unauthorized installer, a minifilter can meticulously inspect `IRP_MJ_CREATE` requests.[^71] If the request originates from an untrusted PID or lacks a specific cryptographic signature, the driver blocks the file handle creation, mirroring the self-protection mechanics of enterprise EDR agents.

### Object Manager Callbacks

As demonstrated by `wdfilter.sys`, `ObRegisterCallbacks` is the ultimate mechanism for process defense.[^17] By intercepting handle creation (`OB_OPERATION_HANDLE_CREATE`), an open-source driver could strip `PROCESS_TERMINATE` and `PROCESS_VM_WRITE` rights from any handle requested against its user-space GUI process or background service.[^17] This effectively renders the application unkillable by standard task management tools or competing software suites, cementing its persistence on the system.

## The Threat Landscape: Vulnerabilities, Exploits, and BYOVD

The modern attack surface for the Windows kernel relies heavily on exploiting legitimate, signed drivers. Because obtaining an EV certificate and passing WHQL is a high barrier, advanced persistent threats (APTs) and ransomware operators utilize the Bring Your Own Vulnerable Driver (BYOVD) technique to bypass HVCI and Secure Boot constraints.[^1]

### Mechanics of a BYOVD Attack

In a BYOVD attack, an adversary who has already achieved administrative privileges drops a legitimate, cryptographically signed, but historically vulnerable driver onto the disk and registers it with the SCM.[^1] Because the driver's signature is technically valid, Driver Signature Enforcement (DSE) allows the payload to map into kernel memory.[^1]

Notable examples of BYOVD exploitation include:

- **Intel iqvw64.sys (CVE-2015-2291):** Exploited by the Scattered Spider threat group, this outdated diagnostics driver was weaponized to execute arbitrary code in the kernel, allowing the attackers to surgically disable EDR agents and maintain persistent, invisible access.[^1]
- **Baidu BdApiUtil.sys (CVE-2024-51324):** Exploited heavily by ransomware operators, this driver contained an Improper Privilege Management flaw.[^33] Attackers crafted an IOCTL (`0x92D`) specifying a target PID.[^33] The driver failed to validate the caller's privileges and blindly executed `ZwTerminateProcess()`, allowing the ransomware to kill native security services from user-space.[^33]

### Privilege Escalation via IOCTL Abuse

Vulnerabilities typically stem from improper validation within the `DeviceIoControl` handler. If an open-source driver exposes an IOCTL that maps physical memory to user-space (utilizing `MmMapIoSpace`) without rigorously verifying that the requested physical address does not overlap with critical kernel structures, catastrophic compromise follows.[^35]

An attacker can target the `EPROCESS` structure of their own calling process, specifically hunting for the `_SEP_TOKEN_PRIVILEGES` field.[^35] By exploiting the vulnerable driver to flip the privilege bytes within this structure, the attacker can enable `SeDebugPrivilege` and `SeLoadDriverPrivilege`, escalating a standard, medium-integrity process to `NT AUTHORITY\SYSTEM` without triggering conventional UAC prompts.[^35]

### Attack Pattern Summary

| Attack Pattern | Mechanism of Abuse | Impact | Required Mitigation |
|---|---|---|---|
| **BYOVD** | Dropping a historically vulnerable, signed driver to disk and loading it via SCM. | Complete EDR evasion, arbitrary kernel code execution.[^1] | Microsoft Vulnerable Driver Blocklist, WDAC enforcement.[^11] |
| **IOCTL Buffer Overflow** | Supplying malformed data to a driver defining `METHOD_BUFFERED` without bounds checking. | Kernel pool corruption, BSOD, or Local Privilege Escalation.[^31] | Strict buffer length validation on all ingress data crossing the trust boundary.[^31] |
| **Arbitrary Memory Mapping** | Using tools like WinRing0 to call `MmMapIoSpace` on critical kernel structures. | Modifying `_SEP_TOKEN_PRIVILEGES` to achieve SYSTEM privileges.[^23] | Deprecating arbitrary mapping; replacing with strict, interpreter-bound operations (e.g., PawnIO).[^23] |
| **Arbitrary Process Termination** | Sending IOCTLs instructing a driver to call `ZwTerminateProcess`.[^33] | Killing EDR sensors, Antivirus, or system logging services.[^33] | IOCTL caller validation via `IoValidateDeviceIoControlAccess` and strong ACLs.[^31] |

### Mitigations

To combat the BYOVD epidemic, Microsoft implemented the Vulnerable Driver Blocklist, which is enabled by default alongside HVCI in Windows 11 2022 and later.[^1] This blocklist revokes the hashes of known vulnerable drivers, explicitly blocking tools like older versions of WinRing0 from executing.[^11] However, the blocklist is inherently reactionary; it only prevents the loading of known vulnerable drivers. True, proactive mitigation requires developers to architect their drivers with an absolute zero-trust IPC handling model.[^31]

## Architectural Recommendations for Open-Source Hardware Control

Based on the synthesis of Microsoft's enforcement mechanisms, the architectural failures of EDR sensors, and the absolute constraints of HVCI, an open-source GPLv2 hardware control and configuration persistence tool must adhere to a strict architectural blueprint. This design minimizes the attack surface while maximizing functionality within the modern Windows 11 kernel.

### 1. Driver Signing and Deployment Strategy

The driver must be submitted for Attestation Signing via the Partner Center using a valid EV Certificate.[^5] Developers must not rely on Test Mode or instructing users to disable Secure Boot, as this compromises the host's entire security posture and breaks trust.[^5]

- **Architecture:** The tool must be physically separated into two components: an Attestation-signed WDF kernel module, and a Session 0 user-mode service that manages persistence and translation.[^5]

### 2. IPC and Access Control Enforcement

The driver must explicitly drop any IOCTL request originating from an unprivileged process.

- **Architecture:** The driver must initialize using `IoCreateDeviceSecure` with an SDDL string that restricts access strictly to the local Administrators group and the SYSTEM account.[^31] The `FILE_DEVICE_SECURE_OPEN` characteristic must be set to protect the device namespace from traversal attacks.[^31] The driver must never use `FILE_ANY_ACCESS`; instead, it must explicitly require `FILE_READ_ACCESS | FILE_WRITE_ACCESS` for its IOCTLs.[^31]

### 3. Hardware Interfacing (HVCI Compliance)

The driver must strictly avoid raw physical memory mapping (`MmMapIoSpace` to arbitrary addresses) and unconstrained `IN`/`OUT` port execution, as these legacy techniques will trigger Defender detections, BSODs, and HVCI violations.[^23]

- **Architecture:** Where possible, the driver must leverage the ACPI subsystem. Use `IOCTL_ACPI_EVAL_METHOD` combined with proper `ACPI_EVAL_INPUT_BUFFER_SIMPLE_INTEGER` structures to invoke BIOS/UEFI-defined methods for fan and temperature control.[^20] For SMBus/I2C communication (e.g., controlling RGB arrays), the driver should utilize the native SpbCx framework rather than attempting manual port bit-banging.[^56]

### 4. Input Sanitization and Sandboxing

The fundamental lesson of the July 2024 CrowdStrike incident must be observed: never parse complex, unverified binary structures directly in the kernel.[^45]

- **Architecture:** If the driver requires dynamic logic (such as PawnIO's bytecode interpreter), the user-mode service must perform cryptographic validation of the bytecode payload before passing it via IOCTL.[^23] Inside the kernel, the interpreter must utilize strict bounds checking and structured exception handling (`__try`/`__except`) to ensure that malformed bytes cannot trigger an out-of-bounds read and a subsequent system crash.

### 5. Configuration State Enforcement

To persist user configurations against aggressive OS updates, the tool should utilize proper kernel callback frameworks to defend its own state.

- **Architecture:** Implement a minimal file system minifilter (`FltRegisterFilter`) or a registry callback (`CmRegisterCallbackEx`).[^67] If the user explicitly opts to lock their hardware control settings, the callback can monitor for `IRP_MJ_WRITE` or `REG_PRE_SET_VALUE_KEY` operations against the tool's specific HKLM paths.[^68] If the operation does not originate from the tool's trusted Session 0 service PID, the driver returns `STATUS_ACCESS_DENIED`, effectively mimicking Microsoft's `ucpd.sys` architecture to rigidly defend the user's preferred state against unauthorized modification.[^14]

## Works Cited

[^1]: Understanding BYOVD Attacks and Mitigation Strategies - Halcyon, accessed March 8, 2026, https://www.halcyon.ai/blog/understanding-byovd-attacks-and-mitigation-strategies

[^2]: Windows 11 security book - Silicon assisted security | Microsoft Learn, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows/security/book/hardware-security-silicon-assisted-security

[^3]: Memory integrity and VBS enablement - Microsoft, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-hvci-enablement

[^4]: Signing Kernel Mode Drivers - DigiCert Knowledge Base, accessed March 8, 2026, https://knowledge.digicert.com/solution/signing-kernel-mode-drivers

[^5]: Driver code signing requirements - Windows drivers | Microsoft Learn, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-reqs

[^6]: WHQL Signing - Do I need it for an internal driver that is not, accessed March 8, 2026, https://community.osr.com/t/whql-signing-do-i-need-it-for-an-internal-driver-that-is-not-distributed/57816

[^7]: WHQL Release Signature - Windows drivers | Microsoft Learn, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/install/whql-release-signature

[^8]: WHQL Driver Testing & Hardware Certification by Microsoft - Apriorit, accessed March 8, 2026, https://www.apriorit.com/qa-blog/631-qa-whql-testing-microsoft-hardware-certification

[^9]: Windows Driver Signing - WinDriver, accessed March 8, 2026, https://windriver.jungo.com/windows-driver-signing/

[^10]: Question about kernel driver update, : r/FanControl - Reddit, accessed March 8, 2026, https://www.reddit.com/r/FanControl/comments/1o6lqqr/question_about_kernel_driver_update/

[^11]: Microsoft recommended driver block rules, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules

[^12]: Windows UCPD velocity driver stops non-Microsoft software from, accessed March 8, 2026, https://www.ghacks.net/2024/04/08/new-sneaky-windows-driver-ucdp-stops-non-microsoft-software-from-setting-defaults/

[^13]: New Windows driver blocks software that changes default web, accessed March 8, 2026, https://www.techzine.eu/news/applications/118510/new-windows-driver-blocks-software-that-changes-default-web-browser/

[^14]: UserChoice Protection Driver -- UCPD.sys - the kolbicz blog, accessed March 8, 2026, https://kolbi.cz/blog/2024/04/03/userchoice-protection-driver-ucpd-sys/

[^15]: Unable to save default application on UPM on Windows Desktop OS, accessed March 8, 2026, https://support.citrix.com/external/article/CTX691100/unable-to-save-default-application-on-up.html

[^16]: Ransom.Win64.HIVE.YABIW - Threat Encyclopedia | Trend Micro (US), accessed March 8, 2026, https://www.trendmicro.com/vinfo/us/threat-encyclopedia/malware/ransom.win64.hive.yabiw

[^17]: Dissecting the Windows Defender Driver - WdFilter (Part 3) :: Up is ..., accessed March 8, 2026, https://n4r1b.com/posts/2020/03/dissecting-the-windows-defender-driver-wdfilter-part-3/

[^18]: Potential threat from software that uses WinRing0 drivers (FanCtrl, accessed March 8, 2026, https://community.frame.work/t/potential-threat-from-software-that-uses-winring0-drivers-fanctrl-openrgb-libre-hardware-monitor-etc/66135

[^19]: Kernel driver i2c-i801, accessed March 8, 2026, https://www.kernel.org/doc/html/v5.8/i2c/busses/i2c-i801.html

[^20]: evaluating-acpi-control-methods-synchronously.md - GitHub, accessed March 8, 2026, https://github.com/MicrosoftDocs/windows-driver-docs/blob/staging/windows-driver-docs-pr/acpi/evaluating-acpi-control-methods-synchronously.md

[^21]: PawnIO.sys Windows process - What is it? - File.net, accessed March 8, 2026, https://www.file.net/process/pawnio.sys.html

[^22]: HackTool:Win32/Winring0 - Issue #3016 - Rem0o/FanControl, accessed March 8, 2026, https://github.com/Rem0o/FanControl.Releases/issues/3016?timeline_page=1

[^23]: Replacing WinRing0 in Fan Control with PawnIO | Poorly Documented, accessed March 8, 2026, https://poorlydocumented.com/2025/09/replacing-winring0-in-fan-control-with-pawnio/

[^24]: Understanding Microsoft Defender's VulnerableDriver WinRing0, accessed March 8, 2026, https://windowsforum.com/threads/understanding-microsoft-defenders-vulnerabledriver-winring0-alert-and-how-to-respond.373544/

[^25]: Why does Defender hate Fan Control? An explanation of Windows, accessed March 8, 2026, https://www.reddit.com/r/FanControl/comments/1j93doq/why_does_defender_hate_fan_control_an_explanation/

[^26]: PawnIO, accessed March 8, 2026, https://pawnio.eu/

[^27]: On Windows, how shall I call arbitrary ACPI methods? - Super User, accessed March 8, 2026, https://superuser.com/questions/1595174/on-windows-how-shall-i-call-arbitrary-acpi-methods

[^28]: IOCTL_ACPI_EVAL_METHOD (acpiioct.h) - Windows drivers, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/acpiioct/ni-acpiioct-ioctl_acpi_eval_method

[^29]: evaluating-a-control-method-that-takes-input-arguments.md - GitHub, accessed March 8, 2026, https://github.com/MicrosoftDocs/windows-driver-docs/blob/staging/windows-driver-docs-pr/acpi/evaluating-a-control-method-that-takes-input-arguments.md

[^30]: IRP_MJ_DEVICE_CONTROL - Windows drivers - Microsoft Learn, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/irp-mj-device-control

[^31]: Windows Security Model for Driver Developers - Windows drivers ..., accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/driversecurity/windows-security-model

[^32]: How to restrict IOCTL and socket communications with a driver to a, accessed March 8, 2026, https://community.osr.com/t/how-to-restrict-ioctl-and-socket-communications-with-a-driver-to-a-verified-process/58082

[^33]: New BYOVD loader behind DeadLock ransomware attack, accessed March 8, 2026, https://blog.talosintelligence.com/byovd-loader-deadlock-ransomware/

[^34]: Understanding Mini-Filter Drivers for Windows Vulnerability, accessed March 8, 2026, https://medium.com/@WaterBucket/understanding-mini-filter-drivers-for-windows-vulnerability-research-exploit-development-391153c945d6

[^35]: [Cracking Windows Kernel with HEVD] Chapter 4: How do we write, accessed March 8, 2026, https://mdanilor.github.io/posts/hevd-4/

[^36]: 5 Common Windows Persistence Techniques and How to Stop Them, accessed March 8, 2026, https://medium.com/@tahirbalarabe2/%EF%B8%8F5-common-windows-persistence-techniques-and-how-to-stop-them-5d6f3b98682d

[^37]: Riot Vanguard - Grokipedia, accessed March 8, 2026, https://grokipedia.com/page/riot_vanguard

[^38]: Is Riot's New Anti-Cheat System, Riot Vanguard, Safe? | PDF - Scribd, accessed March 8, 2026, https://www.scribd.com/document/972040413/Is-Riot-s-new-anti-cheat-system-Riot-Vanguard-safe

[^39]: Vanguard Security Update: Closing the Pre-Boot Gap - Riot Games, accessed March 8, 2026, https://www.riotgames.com/en/news/vanguard-security-update-motherboard

[^40]: Why doesn't Vanguard just turn itself on when it's actually relevant, accessed March 8, 2026, https://www.reddit.com/r/riotgames/comments/1cidecl/why_doesnt_vanguard_just_turn_itself_on_when_its/

[^41]: Vanguard x VALORANT, accessed March 8, 2026, https://playvalorant.com/en-us/news/game-updates/vanguard-x-valorant/

[^42]: BattlEye anti-cheat troubleshooting - Gaijin Support, accessed March 8, 2026, https://support.gaijin.net/hc/en-us/articles/21638218281362-BattlEye-anti-cheat-troubleshooting

[^43]: BattlEye anti-cheat: analysis and mitigation - secret club, accessed March 8, 2026, https://secret.club/2019/02/10/battleye-anticheat.html

[^44]: What Caused the Crowdstrike Outage: A Detailed Breakdown, accessed March 8, 2026, https://www.messageware.com/what-caused-the-crowdstrike-outage-a-detailed-breakdown/

[^45]: Analysis of the CrowdStrike Incident of July 19, 2024, accessed March 8, 2026, https://www.incide.es/files/analisisIncidenteCS-INCIDE-ENG.pdf

[^46]: CrowdStrike Incident - mjcb.ca, accessed March 8, 2026, https://mjcb.ca/blog/2024/07/23/crowdstrike-incident/

[^47]: External Technical Root Cause Analysis -- Channel File 291, accessed March 8, 2026, https://www.crowdstrike.com/wp-content/uploads/2024/08/Channel-File-291-Incident-Root-Cause-Analysis-08.06.2024.pdf

[^48]: Kevin Beaumont: "CrowdStrike have published a v..." - Cyberplace, accessed March 8, 2026, https://cyberplace.social/@GossiTheDog/112835486964050717

[^49]: Crowdstrike's Update Failure Root Cause Analysis, accessed March 8, 2026, https://www.serianu.com/downloads/Crowdstrike%20Falcon%20Failure%20RCA%20-%20Executive%20Summary%20-%208-13-2024.pdf

[^50]: Battle of SKM and IUM - publications.alex-ionescu.co, accessed March 8, 2026, http://publications.alex-ionescu.com/BlackHat/BlackHat%202015%20-%20Battle%20of%20SKM%20and%20IUM.pdf

[^51]: ANALYSIS OF THE ATTACK SURFACE OF WINDOWS 10, accessed March 8, 2026, https://blackhat.com/docs/us-16/materials/us-16-Wojtczuk-Analysis-Of-The-Attack-Surface-Of-Windows-10-Virtualization-Based-Security.pdf

[^52]: Driver Compatibility with Hypervisor-Protected Code Integrity (HVCI), accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/test/hlk/testref/driver-compatibility-with-device-guard

[^53]: Easy Anti-Cheat Driver Incompatible with Kernel-Mode Hardware, accessed March 8, 2026, https://learn.microsoft.com/en-us/answers/questions/3962392/easy-anti-cheat-driver-incompatible-with-kernel-mo

[^54]: If It Looks Like a Rootkit and Deceives Like a Rootkit: A Critical ..., accessed March 8, 2026, https://www.researchgate.net/publication/382681120_If_It_Looks_Like_a_Rootkit_and_Deceives_Like_a_Rootkit_A_Critical_Examination_of_Kernel-Level_Anti-Cheat_Systems

[^55]: When i turn on HVCI and reboots it turn of again automaticly - Microsoft, accessed March 8, 2026, https://learn.microsoft.com/en-us/answers/questions/5758486/when-i-turn-on-hvci-and-reboots-it-turn-of-again-a

[^56]: Enable user mode access to GPIO, I2C, and SPI - UWP applications, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/enable-usermode-access

[^57]: 2BrightSparks Articles - Understanding Sessions in Windows, accessed March 8, 2026, https://www.2brightsparks.com/resources/articles/understanding-sessions-in-windows.html

[^58]: Impact of Session 0 Isolation on Services and Drivers in Windows, accessed March 8, 2026, https://www.coretechnologies.com/WindowsServices/Microsoft-Impact-of-Session-0-Isolation-on-Services-and-Drivers-in-Windows-Vista.pdf

[^59]: Inside Session 0 Isolation and the UI Detection Service -- Part 1, accessed March 8, 2026, https://www.alex-ionescu.com/inside-session-0-isolation-and-the-ui-detection-service-part-1/

[^60]: Windows Session 0 Isolation and Interactive Services Detection?, accessed March 8, 2026, https://kb.firedaemon.com/support/solutions/articles/4000086228-microsoft-windows-session-0-isolation-and-interactive-services-detection

[^61]: Windows Session 0 Isolation & Covenant Integrity - ThatOneSecGuy, accessed March 8, 2026, https://thatonesecguy.medium.com/windows-session-0-isolation-covenant-integrity-7a01ff2fb5ee

[^62]: Persistence with Windows Services | PSBits, accessed March 8, 2026, https://gtworek.github.io/PSBits/services.html

[^63]: Working with tamper protection on Windows devices to protect, accessed March 8, 2026, https://petervanderwoude.nl/post/working-with-tamper-protection-on-windows-devices-to-protect-security-settings/

[^64]: Protecting Windows Registry Directory and Hence Increasing the, accessed March 8, 2026, https://scialert.net/fulltext/?doi=itj.2008.840.849

[^65]: Disabling PPL Protection on Windows Processes | by S12 - Medium, accessed March 8, 2026, https://medium.com/@s12deff/disabling-ppl-protection-on-windows-processes-0cb77a065939

[^66]: Microsoft Security Servicing Criteria for Windows, accessed March 8, 2026, https://www.microsoft.com/en-us/msrc/windows-security-servicing-criteria

[^67]: CmRegisterCallbackEx function (wdm.h) - Windows drivers - Microsoft, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-cmregistercallbackex

[^68]: Filtering Registry Calls - Windows drivers | Microsoft Learn, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/filtering-registry-calls

[^69]: Windows Update Registry Settings: Identify & Manage Updates, accessed March 8, 2026, https://patchmypc.com/blog/windows-update-registry-guide/

[^70]: Enable or Disable "Do not Include Drivers with Windows Updates" in, accessed March 8, 2026, https://www.ninjaone.com/blog/include-drivers-with-windows-updates-in-windows-11/

[^71]: FltRegisterFilter function (fltkernel.h) - Windows drivers - Microsoft, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/fltkernel/nf-fltkernel-fltregisterfilter

[^72]: Registering the Minifilter Driver - Windows - Microsoft Learn, accessed March 8, 2026, https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/registering-the-minifilter-driver

[^73]: Hunting Vulnerable Kernel Drivers - VMware Security Blog, accessed March 8, 2026, https://blogs.vmware.com/security/2023/10/hunting-vulnerable-kernel-drivers.html
