# Why ThisIsMyPC Is and Will Remain Fully GPLv2

AI-written rationale, checked against the code as of 2026-09-01. It records
the licensing decision and the security reasoning behind it.

## The Decision

Every component of ThisIsMyPC, the Avalonia UI, the Session 0 service, the
IPC layer between them, and all security implementation logic, is licensed
under the GNU General Public License v2, version 2 only. The "or any later
version" option is not offered: the repo includes code derived from
ExplorerPatcher and OpenRGB, and both are GPLv2 without that clause, so the
combined work cannot be relicensed under a later version. This is not a
provisional stance or a placeholder until the project matures. It is a
permanent commitment, and the reasoning behind it is rooted in security
engineering, not ideology.

## What the architecture is

Two privilege boundaries, both in this repo:

- A medium-integrity desktop app that elevates at launch (`src/ThisIsMyPC.App`).
- An optional SYSTEM-level background service, Owner Mode
  (`src/ThisIsMyPC.Service`), reached over a hardened named pipe whose message
  envelope lives in `src/ThisIsMyPC.Ipc.Contracts`.

There is no custom kernel driver and none is planned. Hardware modules that
need Ring 0 access (fan control, platform tuning) will consume the upstream
signed PawnIO release (GPLv2 or later) as a separate binary, the way
FanControl and LibreHardwareMonitor do. The project never forks or patches
that driver, so nothing at kernel level is ThisIsMyPC code, and PawnIO's
license places no constraint on this repo either way. Earlier planning assumed an
in-house Attestation-signed driver with a bytecode interpreter; that plan was
retired in favor of upstream PawnIO and the security argument below no longer
depends on it.

## The Argument for Closing the Source

The instinct to keep security-critical code private is understandable. The
IPC authentication logic, the message validation in the service, and the
enforcement paths that write policy as SYSTEM are all components that a
motivated attacker would study to find weaknesses. If the source is public,
the argument goes, adversaries can hunt for gaps at their leisure.

We considered this seriously. We rejected it for the following reasons.

## Closed Source Did Not Save Them

Every major kernel-level security catastrophe in recent history occurred in
closed-source software:

- **CrowdStrike (July 2024):** A proprietary, obfuscated kernel-mode content
  interpreter in `CSagent.sys` was hardcoded to expect 20 input fields. An
  automated update delivered 21. Nobody outside the company could audit the
  parser before it triggered an unhandled `PAGE_FAULT_IN_NONPAGED_AREA`
  exception and bricked millions of systems worldwide.
- **ASUS Armoury Crate (CVE-2025-3464):** A closed-source driver relied on a
  custom SHA-256 allowlist to authenticate calling processes. A hard link
  manipulation attack defeated the entire scheme, a TOCTOU race condition that
  any external reviewer would have identified immediately.
- **Razer Synapse (CVE-2022-47631):** A closed-source SYSTEM service failed to
  secure its installation directory. Attackers planted a malicious DLL and won
  a race condition against the service's own integrity check, achieving
  immediate privilege escalation.
- **Baidu BdApiUtil.sys (CVE-2024-51324):** A closed-source antivirus driver
  exposed an IOCTL that accepted a process ID and called `ZwTerminateProcess`
  without validating the caller's privileges. Ransomware operators used it to
  annihilate every EDR on targeted machines.

Opacity protected none of these systems. It only ensured that the people who
could have caught the flaws before exploitation, independent researchers, the
open-source community, and the users themselves, were locked out.

The two that matter most for this project are Razer and ASUS: a SYSTEM service
and a vendor control app, the same shape as ThisIsMyPC.

## Obscurity Is Measured in Days, Not Years

The threat model assumes an adversary with local administrator access. At that
privilege level, the attacker can load the service binary into any
disassembler, reconstruct the pipe protocol, and fuzz every message type.
Signed Windows binaries are unencrypted, unpacked PE files; Microsoft requires
them to be analyzable. Keeping the source closed buys obscurity measured in
days against a skilled reverse engineer. It buys nothing against the threat
actors documented in the research: Scattered Spider, Mustang Panda, and the
operators behind DeadLock ransomware all routinely reverse closed-source
drivers and services as part of their standard operational workflow.

## Open Source Enables the Supply Chain Guarantees That Matter

The threat modeling research identifies supply chain compromise as one of the
highest-impact risks facing ThisIsMyPC. The SolarWinds SUNBURST attack injected
a backdoor during compilation that was invisible in the source repository. The
XZ Utils backdoor (CVE-2024-3094) hid malicious code exclusively in release
tarballs, diverging from the auditable Git history. The Codecov breach was only
discovered because an external user noticed a checksum mismatch between the
distributed script and the published hash.

The mitigation strategy depends on three properties:

1. **Signed release manifests.** Shipped. Every release publishes `SHA256SUMS`
   with a detached GPG signature from an offline key; the app embeds the public
   key and rejects any update whose manifest, signature, or digest does not
   verify (`GpgManifestUpdateVerifier`, process in
   `docs/release/update-signing.md`). Authenticode signing of the binaries is
   added on release day as a second, independent layer.
2. **Reproducible builds.** Planned. NativeAOT publish already runs in CI from
   a pinned SDK; the remaining work is a frozen build environment so an
   independent party can compile from source and compare bit for bit.
3. **Community auditability.** Any user can inspect the exact code that
   produced the binary running on their machine.

All three properties require the source to be public. A closed-source project
asking users to trust a signed binary is replicating the exact trust model that
SUNBURST exploited. For a tool that runs a SYSTEM service and writes machine
policy, that is not an acceptable posture.

## The Security Architecture Does Not Depend on Secrecy

Every mitigation in the design is built to hold when the attacker has full
knowledge of the implementation. The ones in the code today:

- `FILE_FLAG_FIRST_PIPE_INSTANCE` on the service pipe
  (`Interop.Win32/Ipc/HardenedPipeFactory.cs`) defeats pipe squatting whether
  or not the attacker knows the pipe name.
- The client connects with `SECURITY_IDENTIFICATION` impersonation
  (`Ipc.Contracts/IpcClient.cs`), so a squatted or hijacked server cannot
  impersonate the caller regardless of source visibility.
- Messages are framed with a 1 MiB cap (`IpcProtocol.MaxFrameBytes`) and
  carry a nonce; the envelope is one fixed record, and new message types
  extend it, never change it, so the parser stays auditable.
- The pipe has no write message at all. The three request types are ping,
  service status, and drift report (`IpcMessageTypes`). The service
  re-applies only the changes the app recorded, from its own stored state;
  nothing a client sends can make it write a value.
- The installation path and DLL search order are locked at both entry points
  (`docs/release/hardening-checklist.md`), so the Razer-style planted-DLL
  attack has no place to land.

If any of these mitigations would fail because an attacker read the source,
that would indicate a flawed implementation, not a need for secrecy. Security
through obscurity is not security. It is deferred vulnerability discovery.

## Open Source Attracts Defenders Faster Than Attackers

A project with visible source attracts security researchers who study the
code, identify weaknesses, and file responsible disclosures. A project with
hidden source attracts only the people willing to reverse-engineer it, and
those people are not filing disclosures.

## The Competitive Position

The comparative analysis against Riot Vanguard, BattlEye, and CrowdStrike
Falcon makes the strategic position clear. Those systems achieve extreme
persistence through boot-start ELAM drivers, polymorphic kernel payloads, IAT
hooking, and handle stripping, all operating as opaque black boxes. The
CrowdStrike incident proved what happens when unconstrained capability is
prioritized over systemic safety inside that model.

ThisIsMyPC does not compete on opacity. It competes on transparency,
correctness, and user sovereignty. Full GPLv2 licensing is not a vulnerability
in that position. It is the foundation of it.

## A Note on Apache 2.0 Compatibility

GPLv2 and the Apache License 2.0 are incompatible. Apache 2.0 includes a patent
retaliation clause that GPLv2 treats as an additional restriction under its
Section 7, which means Apache 2.0 code cannot be combined with GPLv2 code into
a single derivative work and legally distributed. GPLv3 resolved this conflict,
but the project cannot move to GPLv3: the dependencies it derives code from are
v2-only (see The Decision), so the combined work stays at v2.

The priority order when an Apache 2.0 library looks useful:

1. **Modular separation.** The GPL's copyleft obligations apply to derivative
   works, not to separate works merely aggregated alongside GPL code. If the
   Apache 2.0 dependency can exist as a discrete standalone binary talking
   over a documented interface, it is not a derivative work.
2. **Avoidance.** Otherwise find an alternative under MIT, BSD, or a GPL-
   compatible license, or build the functionality in the repo.

Package audit, 2026-09-01, from the nuspec license fields: Avalonia,
CommunityToolkit.Mvvm, the Microsoft.Extensions packages, Microsoft.Data.Sqlite,
CsWin32, BouncyCastle, and Velopack are MIT; NLog is BSD-3-Clause. The audit
found Serilog and its three sinks were Apache-2.0 and linked into the shipped
binaries, exactly the conflict this section describes; they were replaced by
NLog the same day. xunit is Apache-2.0 too, but it is test-only and never
distributed, so it does not combine with the program.

## The Commitment

ThisIsMyPC is fully open-source under GPLv2. The app, the service, the IPC
layer, and all security logic. No proprietary modules, no closed components,
no split licensing. The architecture's security is provable, not secret.

This is your PC. You should be able to verify that.
