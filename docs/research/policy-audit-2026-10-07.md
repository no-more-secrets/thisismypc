# Expanded policy audit

Sam's PC is a validation fixture for useful controls and alternate detection paths.
The export is one input, not the full inventory or a configuration to copy.
This audit reads settings. It adds no editable controls and changes no Windows settings.

## Scan coverage

The scan visited all 224 installed ADMX files and all 3,552 policy definitions.
It checked machine and current-user scopes according to each definition's class.
It collected explicit addresses from primary values, option elements, and enabled/disabled lists.
Dynamic list keys were inspected for existing values; 125 definitions contain such lists.
It also walked both conventional policy roots under both scopes:
`Software\Policies` and `Software\Microsoft\Windows\CurrentVersion\Policies`.

| Observation | Count | Meaning |
|---|---:|---|
| Distinct scoped addresses visited | 6,279 | Includes additional values found under policy roots. |
| Definitions with at least one present address | 132 | Candidates, not proof of enabled state. |
| Candidates outside the export | 8 | Includes overlapping definitions and ordinary preference addresses. |
| Values without an installed ADMX address mapping | 116 | Includes security settings, vendor settings, defaults, and possible stale values. |
| Exported records whose scope contradicts installed ADMX | 8 | Scope must be corrected before treating these as configured policies. |
| Access failures | 6 | Unknown, never absent. |

The production `LocalPolicyInspector.ReadCurrentSources` separately read 94 saved machine records.
The common-user, applicable group, and account-specific local files were missing for the scanning account.
Saved policy records, exported records, registry addresses, and ADMX definitions are different units. Their counts must not be combined.

A second pass read the same conventional roots through the 32-bit registry view.
All 246 readable values matched the native view by address, type, and data; three protected keys remained unreadable in this view too.

Private observations and the diagnostic scan script stay under `artifacts/diagnostics/policy-audit/`.
They contain machine data and are not committed.
The [148-row routing ledger](policy-routing-audit.csv) contains only policy identifiers, destinations, evidence status, and historical route coverage.
Every row has a destination. Support remains unresolved until its options, source, and Windows support are verified.

## Additional candidates

| Installed identity | Feature destination | Finding |
|---|---|---|
| FileSys: LongPathsEnabled | Explorer | Long-path behavior has a present address. Check existing behavior and support before adding a control. |
| Power: InboxActiveSchemeOverride_2 | Power | Shares ActivePowerScheme with the exported custom-plan policy. One setting can match both definitions. |
| Power: AllowStandbyStatesAC_2 | Power | Existing sleep policy code already uses ACSettingIndex. Verify the complete feature route before adding anything. |
| Power: AllowStandbyStatesDC_2 | Power | Existing sleep policy code already uses DCSettingIndex. Keep battery and plugged-in state separate. |
| Printing: LegacyDefaultPrinterMode | Explorer / printer behavior | A present preference address does not prove Group Policy configured it. |
| ServiceControlManager: SvchostProcessMitigationEnable | Security | Inspect support and effective mitigation state. |
| tcpip: IP_Stateless_Autoconfiguration_Limits_State | Network & Firewall | Inspect support and applicability before exposing a control. |
| WindowsExplorer: EnableSmartScreen | Security | Overlaps the exported SmartScreen definition. Do not create a duplicate toggle. |

## Export corrections

Rows 74-78, 91, 92, and 137 report machine scope for installed user-only definitions:
Active Desktop controls, attachment antivirus notification, third-party Spotlight suggestions, and legacy Copilot.
An identically named machine value does not prove that the user policy is applied.

The Internet communication umbrella policy needs separate handling.
Installed `ICM.admx` places `NoAutoUpdate=0` in its disabled list and does not place it in the enabled list.
The exported `NoAutoUpdate` address therefore cannot establish that this umbrella policy is enabled.
The live scan found subordinate addresses while the umbrella's primary value was absent.

Some policies use option values without a primary state marker, including diagnostic data and active power plan selection.
Those values require their full option semantics, not a blanket rule that missing primary values mean Not configured.

## Values outside installed definitions

| Family | Values | Required follow-up |
|---|---:|---|
| Edge and Edge Update | 20 | Match current browser templates, supported versions, deprecated policies, and both scopes. |
| Office | 9 | Separate telemetry, feedback, cloud policy, and vendor integration values. |
| Adobe | 9 | Separate useful supported controls from installed defaults and old version paths. |
| System policy paths | 34 | Audit security options, UAC behavior, sign-in settings, and internal defaults. |
| Defender and older Antimalware paths | 13 | Resolve current supported mechanisms, stale aliases, and protection against changes. |
| Other | 31 | Includes alternate diagnostics paths, scope mismatches, and unrelated internal policy data. |

These are address counts, not 116 new controls.
Edge candidates include recommendations, first-run prompts, feedback, diagnostic data, shopping, Rewards, Copilot context, and its chat icon.
Several shopping, Rewards, sidebar, and reporting values already appear in the app's grouped Edge control.
Audit that group's partial state and exact undo before expanding it.

Use Microsoft's [Edge policy reference](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies)
and [Edge Update reference](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-update-policies) for browser definitions.
Use the [security settings reference](https://learn.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/security-policy-settings-reference)
for security settings outside Administrative Templates.
The [CSP catalog](https://learn.microsoft.com/en-us/windows/client-management/mdm/) supplies an additional policy surface that this registry scan does not exhaust.

## Existing controls to audit together

| Feature | Alternate evidence and required checks |
|---|---|
| Promotions and suggestions | Consumer policies, user Spotlight policies, individual ContentDeliveryManager preferences, correct scope, and partial groups. |
| Search and Widgets | Search highlights policy versus search preference; Widgets policies versus TaskbarDa; web search and cloud search separately. |
| Diagnostics and personalization | AllowTelemetry options and both scopes; alternate diagnostics paths; service state; tailored-experiences policy versus preference. |
| Windows Update | NoAutoUpdate, AUOptions, deadlines, active hours, driver exclusion, version targeting, and organization management. |
| AI features | Current and legacy Copilot identities, Recall policy options, both scopes, and separate Paint policies. |
| Power | Active-plan aliases, sleep policy overrides, AC/DC options, throttling, and fast startup. |
| Security | Full option sets, supported Defender mechanisms, partial groups, security options, and refusals when protection blocks changes. |
| Browser controls | Machine/user policies, grouped settings, obsolete identities, and preference versus mandatory policy. |
| Accessibility | Bit-field detection and preservation, including the confirmed Sticky Keys defect. |

The current Taskbar reader reads TaskbarDa without checking Widgets policies.
Search highlights and tailored experiences also have separate preference and policy paths.
The seven-definition policy catalog is not yet connected to those feature controls.
The recent Presets fixes cover NoAutoUpdate and diagnostic level zero only; they do not complete this audit.

Important additions need review even when absent from this PC: update version targeting, delivery bandwidth,
cloud suggestions, app reputation, unwanted-app protection, ransomware protection, and firewall behavior.
These are coverage candidates, not recommendations to weaken protections or apply Sam's settings to another computer.

## Limits and completion criteria

Six reads failed: two graphics configuration addresses, the Security event log descriptor,
Defender Policy Manager, an Advanced Threat Protection task key, and an IPsec policy key.
Their contents remain unknown. Do not change permissions to complete an inventory.

Definition matching uses the native 64-bit registry view and installed Windows templates.
The additional 32-bit pass covers the two conventional roots, not every address outside them.
This audit does not exhaust vendor templates, domain policy, MDM precedence, or security databases outside these roots.
Present registry values can be defaults, preferences, stale entries, or policies written by another tool.
Neither this scan nor an ADMX match proves Windows honors a setting on this build and edition.

For every control, verify supported scope and options, compare preference and policy sources, preserve unrelated fields,
and test exact undo from missing, alternate, and conflicting states.
Complete the unresolved ledger before claiming comprehensive policy coverage.

## Functional assessment and proposed controls

The [functional assessment](policy-function-assessment.csv) evaluates every exported record individually, including direct, partial, and related routes.
Each row states the behavior, recommendation, reason, feature destination, template identity, and any additional web evidence.
These are product recommendations, not new controls or proof that Windows currently enforces the exported setting.

| Recommendation | Export records | Meaning |
|---|---:|---|
| Add | 26 | Useful behavior that deserves an editable control, after support and state verification. |
| Extend | 26 | Integrate policy or missing options into an existing feature rather than create a duplicate card. |
| Advanced | 52 | Potentially useful specialist behavior; keep behind its feature's advanced options or a dedicated workflow. |
| Skip | 35 | Do not add an editor for this identity: obsolete, inapplicable, internal bookkeeping, or no useful desktop workflow. |
| Investigate | 9 | Functional identity or current applicability remains uncertain; do not ship a guessed control. |

Counts measure records, not controls. Several rows should share one editor.
The 117 historical gaps remain the original route count; the recommendations also evaluate 31 records with some existing coverage.
Recognize unsupported and skipped records in inspection results where useful, without offering misleading toggles.
An Add decision recommends exposing a choice. It does not recommend enabling or disabling that choice by default.

### Evidence and limits

All 148 descriptions and option definitions were read from installed Microsoft ADMX/ADML files.
The [template manifest](policy-template-evidence.csv) records SHA-256 hashes for the 39 template pairs used by the exported records.
Template paths are relative to `%WINDIR%/PolicyDefinitions`.
No private configured values are included.

Installed templates establish intended semantics, not actual enforcement on every Windows edition or build.
Microsoft's current documentation was checked for material deprecation, destructive effects, and ambiguous support.
CSP applicability is evidence about that delivery route; it must not automatically become a claim about Group Policy support.
Where CSP and installed template support differ, feature availability and edition/build support remain implementation gates.

This assessment covers the 148 export records and the eight additional installed-template candidates below.
The 116 unmapped values and six unreadable locations remain separate inventory work, not silently assessed policies.
No policies were applied. Apply, restart, management precedence, and undo require controlled tests before shipping an editor.

### First implementation group

Fix source-aware state and existing controls before adding broad coverage.
The existing seven-definition policy reader gives suggestions and personalization a concrete starting point.
Use saved policy, registry state, and actual feature state as separate evidence; do not reduce them to one unchecked boolean.

| Feature | Recommended change | Export rows |
|---|---|---|
| Suggestions and personalization | Integrate tips, consumer content, third-party Spotlight, Search highlights, and tailored-experience policies. Keep different effects separately selectable. | 9-12, 92, 135, 146 |
| Windows Update | Complete automatic-update modes and scheduling, then add feature/quality deadlines and policy-aware active hours. | 64-67 |
| Widgets | Distinguish feature permission, Board availability, lock-screen Widgets, and taskbar button visibility. Gate preview-only support. | 52-54 |
| AI | Add individual Paint AI choices and a supported Settings agent option. Audit Recall deletion and obsolete Copilot claims first. | 56-58, 132-134, 137, 147-148 |
| Privacy | Add clipboard history, cloud search, compatibility inventory, and Find My Device. Reconcile speech, location, and clipboard-sync policies with preferences. | 43, 73, 83-84, 89, 93-94 |
| Power and Explorer | Add power throttling and detailed status messages. Integrate Fast Startup policy with its preference rather than invert its meaning. | 1, 5-6 |

These groups are implementation order, not a requirement to delay beta until every candidate is complete.
Build and edition support must determine availability. Sam's preview build is not evidence of support on the minimum release build.

### Security choices worth adding

Expose a focused Security surface for unwanted-app protection, real-time protection, cloud protection, sample submission, and Block at First Sight.
Add Controlled Folder Access, malicious-domain protection, SmartScreen modes, phishing warnings, app-source restrictions, and scan priority.
Windows Security notification modes should distinguish informational messages from critical alerts.
Rows 17, 19, 23, 102-105, 118, 138-141, and 145 describe these choices.

Controlled Folder Access needs allowed-app and protected-folder management, not an isolated toggle that leaves blocked applications unexplained.
Cloud features need dependency checks; turning samples off can disable Block at First Sight.
The scan-priority setting changes scheduling priority, not a percentage CPU limit.
Notifications, exclusions, local overrides, and schedules should share their feature editor rather than produce dozens of unrelated cards.

Use supported Defender management and observed protection state.
Tamper protection can block Group Policy changes even when a management tool appears successful.
The older DisableAntiSpyware switch is not a reliable consumer control.
See Microsoft's [tamper-protection behavior](https://learn.microsoft.com/en-us/defender-endpoint/tamper-protection-troubleshoot)
and [legacy DisableAntiSpyware guidance](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/security-malware-windows-defender-disableantispyware).

### Findings that change the proposed scope

- Rows 61 and 63, immediate installation and recommended updates, have no effect on Windows 10 or 11.
  Do not add them. See Microsoft's [legacy update policy guidance](https://learn.microsoft.com/en-us/windows/deployment/update/avoid-legacy-policy-configurations).
- Current deadline behavior differs from older template wording. Patched Windows 11 respects automatic-update scheduling before expiry, then overrides it.
  See [deadline behavior](https://learn.microsoft.com/en-us/windows/deployment/update/wufb-compliancedeadlines).
- Row 6 does not offer symmetric Fast Startup on/off behavior. Disabled means use the local setting.
- Rows 56, 58, and 147 can delete Recall snapshots. Restoring policy bytes cannot recover those snapshots.
  Separate the reversible configuration from any destructive consequence and obtain informed confirmation before applying it.
  See [Recall component availability](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#allowrecallenablement)
  and [snapshot policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#disableaidataanalysis).
- Row 51 deploys Secure Boot certificates to firmware. Installed help explicitly says Windows cannot undo the deployment.
  It requires a maintenance workflow, not the reversible-changes pipeline.
- Rows 137 and 148 target legacy integrated Copilot. They must not stand in for management of the current app.
  See [current Copilot management](https://learn.microsoft.com/en-us/windows/client-management/manage-windows-copilot).
- Rows 74-78 target pre-Vista Active Desktop. They do not control Windows 11 desktop icons or wallpaper.
- Row 79 targets the old Internet Connection Firewall, replaced by Windows Firewall in XP SP2.
- Row 100 controls Windows Server automatic exclusions, not this client app's target.
- Rows 86-87 describe historical activity-history cloud behavior. Windows 11 stopped sending that history after January 2024 updates.
  Skip the upload editor and verify any remaining local Activity Feed consumer before extending that control.
  See [activity-history changes](https://support.microsoft.com/en-us/windows/privacy/windows-activity-history-and-your-privacy).
- Row 90 disables the manually used, deprecated Steps Recorder. It is not a background activity-recording service.
  See [Windows deprecations](https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features).
- Row 60 belongs to legacy Windows Media Player. Its title and registry name indicate updates, but both template and online help describe first-run dialogs.
  Keep it unresolved rather than implement either interpretation. See the [conflicting Microsoft entry](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsmediaplayer#disableautoupdate).
- Rows 53-55 and 57 need explicit availability gates. Current CSP pages label several as preview features; Recall export is EEA-only.
  See [Widgets policies](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests)
  and [Windows AI policies](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai).

### Additional installed-template candidates

These eight candidates are outside the 148-record count. Their template explanations were also read individually.

| Candidate | Functional effect | Recommendation |
|---|---|---|
| Win32 long paths | Permits paths beyond the traditional limit for applications that support long paths. | Add to Explorer; do not promise every application or shell operation supports them. |
| Built-in active power plan | Forces one of the standard Windows power plans. | Extend the existing plan selector; shares the custom-plan policy address. |
| Plugged-in standby permission | Permits or prohibits S1-S3 standby while plugged in. | Extend existing Power detection; do not claim it universally controls Modern Standby. |
| Battery standby permission | Permits or prohibits S1-S3 standby while on battery. | Same Power control with separate AC/DC state. |
| Default printer management | Stops Windows managing the default printer when enabled. | Add to printer behavior; it is a preference and does not itself choose a printer. |
| Svchost process mitigations | Requires Microsoft-signed loaded binaries and blocks dynamic code in applicable service hosts. | Advanced security option only after service compatibility and recovery testing. |
| Stateless IP autoconfiguration limits | Caps autoconfigured addresses and routes; disabling removes those limits. | Skip ordinary editor; no demonstrated personal-PC workflow requiring it. |
| Explorer SmartScreen definition | Describes the same downloaded-app reputation behavior as row 141. | Use one SmartScreen editor; do not duplicate the policy. |

### Acceptance gates for proposed controls

Every editor needs correct scope, valid option ranges, supported build/edition, source precedence, and an effective-state reader.
Preserve missing values, value types, unrelated options, saved policy records, and exact before-state for undo.
Allow Not configured where it returns control to preferences; do not translate it universally into Disabled.
Distinguish allowed, enabled, unavailable, blocked by policy, and unknown.
Do not claim a restart is required without evidence; use the established conditional wording otherwise.
Destructive consequences require their own workflow and cannot inherit a reversible label from a registry write.
