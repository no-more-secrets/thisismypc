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
