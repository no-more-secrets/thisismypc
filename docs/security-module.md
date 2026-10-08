# Security module

The Security page has four sections and 16 policy controls:

| Section | Controls |
| --- | --- |
| Sign-in | Require Ctrl+Alt+Delete before sign-in |
| Defender | Real-time protection, potentially unwanted applications, cloud protection, sample submission, block at first sight, controlled folder access, network protection, scan priority |
| App protection | Downloaded app reputation, installation sources, phishing protection, malicious destination warnings, password reuse warnings, unsafe password storage warnings |
| Notifications | All alerts, critical alerts only, or no alerts |

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

- Saved local registry policies are read and compared. A saved policy, conflicting
  source, or unreadable source blocks direct editing. This module does not write
  `Registry.pol`, the local security database, domain policy, or MDM configuration.
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

Tests use a fake registry for writes, edition gating, before-state checks, undo,
saved-policy blocking, Broker authorization, and preset export. The live screenshot
test only opens and reads Security; it never applies a choice.
