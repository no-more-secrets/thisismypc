# Security module

The Security page has seven sections and 52 policy controls.
They cover 50 of the 62 assessed Security policy candidates. Assessment records
and controls differ because some policies share a control and three controls
were added beyond that inventory.

| Section | Controls |
| --- | --- |
| Sign-in | Require Ctrl+Alt+Delete before sign-in |
| Defender | Real-time protection, potentially unwanted applications, cloud protection, sample submission, block at first sight, controlled folder access, network protection, behavior monitoring, file activity monitoring, download scanning, script scanning, process scanning when protection resumes, cloud blocking level |
| Scanning | Scan priority, heuristic detection, archive scanning, network file scanning, intelligence checks before scheduled scans |
| Threat updates | Intelligence updates on battery and at Defender service startup |
| App protection | Downloaded app reputation, installation sources, phishing protection, malicious destination warnings, password reuse warnings, unsafe password storage warnings |
| Notifications | All alerts, critical alerts only, or no alerts |
| Policy overrides | Local list merging and preference precedence for six Defender features |

The Defender section also includes startup priority, automatic remediation, and
file hashes. Scanning includes file direction, schedule randomization, restore
points before cleaning, catch-up scans, and scanning through file-system links.
Threat updates include rapid intelligence, Microsoft Update, false-positive
corrections, and scanning after updates. Notifications includes Defender diagnostic
reports, enhanced notifications, and restart notices. App protection also controls
whether users can edit exploit protection locally.

### Remaining inventory

Twelve assessed candidates remain: boot-driver initialization (row 2), two
BitLocker startup policies (7, 8), exploit-protection XML (41), Secure Boot
certificate deployment (51), Enhanced Storage activation (81), LSASS protection
(82), attachment antivirus integration (91), cloud timeout (106), download scan
size (115), intelligence catch-up interval (128), and intelligence update time (130).
The first group needs dedicated protection/recovery workflows. Numeric limits and
time choices need editors with validation, not a short list of arbitrary presets.
TPM owner authorization (88) remains under investigation, outside these 62 candidates.

### Expanded Defender policies, 2026-10-08

Twenty-four additional controls cover rows 16, 20, 31, 35, 39, 40, 96, 97,
98, 99, 101, 107, 110-114, 119, 121, 122, 124, 125, 131, and 143.
Every mapping was checked against the installed WindowsDefender.admx and
WindowsDefenderSecurityCenter.admx, including their English help. Independent
fixtures test paths, positive-label polarity, and scan-direction values.

Catch-up scan controls require an existing scan schedule; this page does not yet
create schedules. Randomization uses the configured Windows window. Restore points
depend on System Protection. MAPS features require cloud protection. Reboot notices
apply to Defender's UI-only mode, not Windows Update. Reparse-point scanning can
slow scans. The notification controls set different policies; Windows Security's
all-alert suppression can still hide Defender activity notices.

Policy overrides select a preference source, not protection state. Explicit local
preference is 1; Group Policy is 0. These controls do not modify the preference.
The feature cards continue to show configured policy, not the effective Defender
preference. Local-list merging is separately inverse-valued: 0 merges lists,
1 uses only policy lists. Exploit-protection editing also uses inverse values;
it neither enables protection nor imports an exploit-protection configuration.

### Added Defender policies, 2026-10-08

These twelve controls cover assessment rows 21, 22, 24, 25, 36, 37, 38, 108,
116, 120, 126, and 127. Paths below are relative to
`HKLM\SOFTWARE\Policies\Microsoft\Windows Defender`. All values are DWORD.
Missing remains Not configured rather than an inferred Windows default.

| Control | Subkey and value | On / Off or choices |
| --- | --- | --- |
| Monitor program behavior | `Real-Time Protection\DisableBehaviorMonitoring` | 0 / 1 |
| Monitor file and program activity | `Real-Time Protection\DisableOnAccessProtection` | 0 / 1 |
| Scan downloads and attachments | `Real-Time Protection\DisableIOAVProtection` | 0 / 1 |
| Scan scripts | `Real-Time Protection\DisableScriptScanning` | 0 / 1 |
| Scan processes when protection resumes | `Real-Time Protection\DisableScanOnRealtimeEnable` | 0 / 1 |
| Cloud blocking level | `MpEngine\MpCloudBlockLevel` | Default 0, Moderate 1, High 2, High plus 4, Zero tolerance 6 |
| Detect unfamiliar threats | `Scan\DisableHeuristics` | 0 / 1 |
| Scan archive files | `Scan\DisableArchiveScanning` | 0 / 1 |
| Scan network files | `Scan\DisableScanningNetworkFiles` | 0 / 1 |
| Check for updates before scheduled scans | `Scan\CheckForSignaturesBeforeRunningScan` | 1 / 0 |
| Allow threat updates on battery | `Signature Updates\DisableScheduledSignatureUpdateOnBattery` | 0 / 1 |
| Check for threat updates at startup | `Signature Updates\UpdateOnStartUp` | 1 / 0 |

Mappings and boolean polarity match the installed WindowsDefender.admx and its
English help. Cloud blocking requires MAPS. Higher levels can produce false
positives; Zero tolerance blocks unknown executables. Intelligence checks before
scans apply to scheduled scans only. Startup means Defender service startup.
Archive and network scanning retain the scope qualifications shown in their cards.
These controls edit configuration; live enforcement remains unverified.

## State and editing

These are configured policies, not an antivirus health report. Missing values show
**Not configured**, never Off. Known values show their complete option. Paired
values must match a complete choice; other readable combinations show a custom
policy state. Wrong types, unknown values, and failed reads block editing.
Cards show edition requirements and a short policy notice. Full source explanations
appear behind the information hover. Do not repeat the selected policy value below
its control or add an unverified-Pro caveat to each card.

Choices enter the existing pending queue. Every target carries its exact typed
before-value, including absence. The Broker accepts only catalogued addresses,
types, values, and edition metadata. Apply checks the before-value again and
reads the result back. History undo uses the same checks. A failed or uncertain
mutation uses the existing reconciliation workflow.

Queue edits reuse the displayed snapshot instead of scanning every policy again.
Creating a choice and staging it still validate fresh policy state independently.
The page refreshes live values after Apply finishes or when the page reloads.

Preset export stores a complete choice identifier. This preserves companion
values, such as SmartScreen Warn versus preventing bypass. History export splits
Security choices within an Apply batch before encoding them.

## Edition policy

Ctrl+Alt+Delete requires **Windows Pro or higher**. The other controls conservatively
require **Windows Enterprise or Education** until Pro behavior is verified for each
policy. This is the product's evidence threshold, not a claim that Microsoft
documents every policy as Enterprise-only. Home shows every control but disables
editing. Edition checks also apply to presets and the pending queue.

## Limits

- Administrative-template controls edit saved local computer policy through
  `IGroupPolicyObject`, which saves `Registry.pol` and updates GPO metadata.
  Each queued target captures the saved instruction and current registry value.
  Apply and undo verify both; failures attempt to restore both. Not configured
  removes the target's saved instruction and current value. Unrelated settings remain.
- Unknown sources, saved/current conflicts, duplicate instructions, and broad
  deletion directives block editing. Individual value deletion markers are supported
  and restored on undo. Changes in another editor before Save are rejected.
- This module does not write user policy, the local security database, domain
  policy, or MDM configuration. It does not bypass tamper protection.
- Ctrl+Alt+Delete reads the current `DisableCAD` registry value. It does not inspect
  the local security database for ownership or a future refresh.
- Defender tamper protection can reject policy changes. An accepted registry
  write does not prove that protection changed. No removal tool or protection
  bypass runs through this module.
- Cloud, sample submission, phishing, and Defender prerequisites appear in the
  descriptions. Selecting a dependent policy does not silently change prerequisites.
- Runtime behavior across Windows builds, editions, third-party antivirus, domain
  management, and MDM still needs live-system testing.

## Sources

Registry mappings and choice values were checked against the installed Windows
templates: WindowsDefender.admx, SmartScreen.admx, WebThreatDefense.admx, and
WindowsDefenderSecurityCenter.admx. The catalog preserves legacy Basic MAPS values
without claiming they provide a distinct modern protection level.

- [Microsoft: interactive logon and Ctrl+Alt+Delete](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/interactive-logon-do-not-require-ctrl-alt-del)
- [Microsoft: tamper protection troubleshooting](https://learn.microsoft.com/en-us/defender-endpoint/troubleshoot-problems-with-tamper-protection)
- [Microsoft: tamper protection overview](https://learn.microsoft.com/en-us/defender-endpoint/tamper-protection-overview)
- [Microsoft: controlled folder access](https://learn.microsoft.com/en-us/defender-endpoint/customize-controlled-folders)
- [Microsoft: Defender scan options](https://learn.microsoft.com/en-us/defender-endpoint/configure-advanced-scan-types-microsoft-defender-antivirus)
- [Microsoft: cloud protection and blocking levels](https://learn.microsoft.com/en-us/defender-endpoint/cloud-protection-configure)
- [Microsoft: Defender administrative-template policies](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-microsoftdefenderantivirus)

Tests use a fake registry for writes, edition gating, before-state checks, undo,
saved-policy transactions, Broker authorization, and preset export. The live screenshot
test only opens and reads Security; it never applies a choice.

The native writer uses a dedicated STA thread and the Windows machine policy lock.
Automated tests cover the transaction through a fake policy session. Elevated native
Save, concurrent Group Policy Editor use, and live undo still need Windows acceptance
testing; passing unit tests does not establish those results.
