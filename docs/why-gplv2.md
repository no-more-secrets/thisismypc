# Why ThisIsMyPC Is and Will Remain Fully GPLv2

## The Decision

Every component of ThisIsMyPC — the Avalonia UI, the Session 0 service, the kernel driver, and all security implementation logic — is licensed under the GNU General Public License v2. This is not a provisional stance or a placeholder until the project matures. It is a permanent architectural commitment, and the reasoning behind it is rooted in security engineering, not ideology.

## The Argument for Closing the Source

The instinct to keep security-critical code private is understandable. ThisIsMyPC's full architecture spans three privilege boundaries: a medium-integrity desktop GUI (shipped), a SYSTEM-level background service (v1.0, Epic 28), and an Attestation-signed Ring 0 kernel driver utilizing the PawnPP bytecode interpreter (Phase 2+, Epic 22). The IPC authentication logic, IOCTL dispatch validation, bytecode verifier bounds checks, and callback target lists are all components that a motivated attacker would study to find weaknesses. If the source is public, the argument goes, adversaries can hunt for gaps at their leisure.

We considered this seriously. We rejected it for the following reasons.

## Closed Source Did Not Save Them

Every major kernel-level security catastrophe in recent history occurred in closed-source software:

- **CrowdStrike (July 2024):** A proprietary, obfuscated kernel-mode content interpreter in `CSagent.sys` was hardcoded to expect 20 input fields. An automated update delivered 21. Nobody outside the company could audit the parser before it triggered an unhandled `PAGE_FAULT_IN_NONPAGED_AREA` exception and bricked millions of systems worldwide.

- **ASUS Armoury Crate (CVE-2025-3464):** A closed-source driver relied on a custom SHA-256 allowlist to authenticate calling processes. A hard link manipulation attack defeated the entire scheme — a TOCTOU race condition that any external reviewer would have identified immediately.

- **Razer Synapse (CVE-2022-47631):** A closed-source SYSTEM service failed to secure its installation directory. Attackers planted a malicious DLL and won a race condition against the service's own integrity check, achieving immediate privilege escalation.

- **Baidu BdApiUtil.sys (CVE-2024-51324):** A closed-source antivirus driver exposed an IOCTL that accepted a process ID and called `ZwTerminateProcess` without validating the caller's privileges. Ransomware operators used it to annihilate every EDR on targeted machines.

Opacity protected none of these systems. It only ensured that the people who could have caught the flaws before exploitation — independent researchers, the open-source community, and the users themselves — were locked out.

## Obscurity Is Measured in Days, Not Years

The threat model for ThisIsMyPC's kernel driver assumes an adversary with local administrator access. At that privilege level, the attacker can load the Attestation-signed `.sys` binary into any disassembler, reconstruct the IOCTL dispatch table, map the PawnPP interpreter's execution paths, and fuzz every input surface. Signed Windows drivers are unencrypted, unpacked PE binaries — Microsoft requires them to be analyzable. Keeping the source closed buys obscurity measured in days against a skilled reverse engineer. It buys nothing against the threat actors documented in the research: Scattered Spider, Mustang Panda, and the operators behind DeadLock ransomware all routinely reverse closed-source drivers as part of their standard operational workflow.

## Open Source Enables the Supply Chain Guarantees That Matter

The threat modeling research identifies supply chain compromise as one of the highest-impact risks facing ThisIsMyPC. The SolarWinds SUNBURST attack injected a backdoor during compilation that was invisible in the source repository. The XZ Utils backdoor (CVE-2024-3094) hid malicious code exclusively in release tarballs, diverging from the auditable Git history. The Codecov breach was only discovered because an external user noticed a checksum mismatch between the distributed script and the published hash.

ThisIsMyPC's mitigation strategy against these attacks depends on three properties:

1. **Reproducible builds:** NativeAOT compilation will occur in a frozen, containerized CI/CD environment. Independent researchers will be able to compile from source and verify the output is bit-identical to the distributed binary.

2. **Dual-layer signature verification:** Every release will carry both an Authenticode signature and a detached GPG signature verified against a public key hardcoded in the source.

3. **Community auditability:** Any user can inspect the exact code that produced the binary running on their machine.

All three properties require the source to be public. A closed-source project asking users to trust a signed binary is replicating the exact trust model that SUNBURST exploited. For a tool that loads a kernel driver and runs a SYSTEM service, that is not an acceptable posture.

## The Security Architecture Does Not Depend on Secrecy

Every mitigation in ThisIsMyPC's design — implemented or planned — is built to hold even when the attacker has full knowledge of the implementation:

- `FILE_FLAG_FIRST_PIPE_INSTANCE` defeats pipe squatting whether or not the attacker knows the pipe name.
- `SECURITY_IDENTIFICATION` impersonation limits prevent token theft regardless of source visibility.
- `METHOD_BUFFERED` with strict `InputBufferLength` validation stops IOCTL buffer overflows even if the attacker has read the dispatch routine.
- Cryptographic bytecode signing ensures the PawnPP interpreter rejects unauthorized payloads even if the attacker understands the verifier logic.
- Static callback target pinning prevents `CmRegisterCallbackEx` weaponization even if the attacker knows exactly which keys are protected.

If any of these mitigations would fail because an attacker read the source code, that would indicate a flawed implementation — not a need for secrecy. Security through obscurity is not security. It is deferred vulnerability discovery.

## Open Source Attracts Defenders Faster Than Attackers

A project with visible source attracts security researchers who study the code, identify weaknesses, and file responsible disclosures. A project with hidden source attracts only the people willing to reverse-engineer it — and those people are not filing disclosures. The eBPF ecosystem, which faces an analogous challenge of safely executing user-supplied bytecode inside a kernel, thrives on open-source development precisely because community audit pressure catches bugs faster than adversaries can weaponize them.

## The Competitive Position

The comparative analysis against Riot Vanguard, BattlEye, and CrowdStrike Falcon makes the strategic position clear. Those systems achieve extreme persistence through boot-start ELAM drivers, polymorphic kernel payloads, IAT hooking, and handle stripping — all operating as opaque black boxes. The CrowdStrike incident proved what happens when unconstrained capability is prioritized over systemic safety inside that model.

ThisIsMyPC does not compete on opacity. It competes on transparency, correctness, and user sovereignty. Full GPLv2 licensing is not a vulnerability in that position. It is the foundation of it.

## A Note on Apache 2.0 Compatibility

GPLv2 and the Apache License 2.0 are technically incompatible. Apache 2.0 includes a patent retaliation clause that GPLv2 treats as an "additional restriction" under its Section 7, which means Apache 2.0 code cannot be combined with GPLv2 code into a single derivative work and legally distributed. GPLv3 resolved this conflict explicitly, making the two licenses fully compatible — but upgrading from GPLv2 to GPLv3 is only possible if the upstream GPL dependencies include the "or (at your option) any later version" clause in their license headers. A bare GPLv2 `LICENSE` file that identifies itself as "Version 2, June 1991" with no "or later" language anywhere in the source headers means GPLv2-only under Section 9 of the GPL.

This matters because ThisIsMyPC may, as it grows, encounter valuable libraries or dependencies released under Apache 2.0. Rather than treat this as a blocking constraint, we approach it through a clear priority order:

1. **Audit first.** Before assuming incompatibility, we verify whether our GPLv2 dependencies actually carry the "or later" clause. Many community-driven open-source projects adopt it by default following the FSF's recommendation. If the clause is already present, the combined work can be distributed under GPLv3, and Apache 2.0 compatibility is resolved without any action from anyone.

2. **Modular architecture second.** The GPL's copyleft obligations apply to derivative works, not to separate works that are merely aggregated alongside GPL code. ThisIsMyPC's architecture already enforces clean privilege boundaries between the GUI, the Session 0 service, and the kernel driver. If an Apache 2.0 dependency only needs to exist as a discrete, standalone module — a separate binary, separate build target, separate distribution artifact that communicates with the main codebase through a well-defined interface such as IPC or a plugin boundary rather than being statically linked or compiled into the same executable — there is a strong argument that it constitutes a separate work. The licensing question becomes irrelevant for that module. We will use this natural architectural separation to accommodate Apache 2.0 libraries where needed without compromising the GPL integrity of the core.

3. **Upstream engagement third.** If a critical GPLv2 dependency is locked to v2-only and modular separation is not feasible for a particular Apache 2.0 library, we can approach the GPL dependency maintainers about adding the "or later" clause to their headers. This is a minimal change — a one-line amendment to file headers — that does not alter their current licensing in practice. Everything that was GPLv2 remains GPLv2; it simply grants downstream projects the option to combine under GPLv3 when compatibility demands it.

4. **Avoidance last.** If none of the above paths resolve the conflict for a specific dependency, we avoid the Apache 2.0 code entirely and either find an alternative or build the functionality ourselves.

The core commitment is non-negotiable: the main architecture — GUI, service, driver, and all security logic — remains GPLv2. Licensing compatibility is an engineering constraint to be managed through careful dependency auditing and architectural discipline, not a reason to fragment the project's licensing posture.

## The Commitment

ThisIsMyPC is fully open-source under GPLv2. The GUI, the service, the driver, and all security logic. No proprietary modules, no closed components, no split licensing. The architecture's security is provable, not secret.

This is your PC. You should be able to verify that.
