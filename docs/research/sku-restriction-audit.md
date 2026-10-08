# SKU Restriction Audit (Home/Pro/Enterprise/Education)

Which shipped tweaks are edition-dependent, per the **official Policy CSP edition
tables** (learn.microsoft.com, fetched 2026-08-29). These feed the
`SettingEnforcement.SkuRestriction` tags. These tags drive card requirements
and set-preview notices. Successful writes remain undoable but do not establish policy support.

## Current implementation (2026-10-07)

`SkuRestriction` stores the minimum supported edition tier: Home < Pro < Enterprise/Education.
Enterprise and Education are equivalent for this product. A Pro minimum excludes Home;
an Enterprise or Education minimum excludes Home and Pro.

Cards show the required edition and disable their switches below that tier. The Pro label reads "Requires Windows Pro or higher". Descriptions remain readable.
An unknown edition disables policy controls with an unverified-support notice.
Presets skip restricted entries. The interactive queue checks every descriptor before staging and again before applying.
Undo and the restoration executor remain separate from the interactive queue checks.

Untagged Microsoft policy registry paths default to a Pro requirement. This is a conservative product rule,
not evidence that Home ignores every policy. An explicit Home tag requires evidence for the specific method.
Ordinary preferences remain available. Defender policy paths default to Enterprise/Education until Pro support is verified.
Consumer-content exclusions and diagnostic data Off require Enterprise/Education. Required diagnostic data remains available on Pro.
Group controls must declare restrictions for companion policies, including Bing search.
A successful registry write does not prove policy enforcement. New policy editors and build-specific support remain pending.

The source table below records the 2026-08-29 research, not a complete current coverage audit.
The CSP tables describe managed-policy applicability; direct registry behavior can differ.
Build-specific enforcement and organization-managed behavior still need validation.

## Original policy audit, now tagged with a Pro minimum

| Module setting | Policy value(s) | CSP source (editions column: Pro/Ent/Edu/IoT only) |
|---|---|---|
| WU `version-pin` | TargetReleaseVersion, ProductVersion, TargetReleaseVersionInfo | [policy-csp-update](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update) |
| WU `auto-update-mode` | AUOptions (CSP: AllowAutoUpdate) | policy-csp-update |
| WU `no-auto-reboot` | NoAutoRebootWithLoggedOnUsers (legacy AU family; same policy processing as AllowAutoUpdate) | policy-csp-update (family inference ; no dedicated CSP row) |
| WU `exclude-drivers` | ExcludeWUDriversInQualityUpdate | policy-csp-update |
| WU `delivery-optimization` | DODownloadMode | [policy-csp-deliveryoptimization](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-deliveryoptimization) |
| Annoyances `copilot` | TurnOffWindowsCopilot (deprecated by MS; User scope) | [policy-csp-windowsai](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai) |
| Annoyances `recall` | AllowRecallEnablement, DisableAIDataAnalysis | policy-csp-windowsai |
| Annoyances `activity-history` | EnableActivityFeed | [policy-csp-privacy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy) |

Higher-tier settings can use an Enterprise or Education minimum. Current Annoyances examples
include desktop Spotlight collection and consumer features.

## Audited and deliberately NOT tagged

| Setting | Why no tag |
|---|---|
| `bing-search` (DisableSearchBoxSuggestions, HKCU policy) | ADMX applicability lists Pro+, but consistent field reports confirm the HKCU registry value is honored on Home. A "Windows ignores this" notice would likely be false. Revisit if evidence changes. |
| `edge-shortcuts` (EdgeUpdate policies) | Applied by Edge's updater on every edition. |
| `edge-sidebar` (Edge HubsSidebarEnabled) | Edge browser policy honored on unmanaged devices of any edition (not on Edge's protected-policies list). |
| All HKCU preference values (ContentDeliveryManager, GameDVR, GameBar, accessibility, advertising-id, copilot-button, UserProfileEngagement) | User preferences, not policy-gated; edition-independent. |
| HAGS (GraphicsDrivers HwSchMode) | Driver/hardware gated, not edition gated. |
| Services / scheduled tasks / startup entries (Clean Boot) | SCM and Task Scheduler are edition-independent. |
| Shell / context-menu / power settings | Non-policy registry + powrprof ; edition-independent. |
| Telemetry note | `AllowTelemetry=0` is respected only on Enterprise/Education (control-surface research L28). The current model can express that tier. Audit value-specific telemetry behavior separately from policy availability. |

## Where the tags live

- `WindowsUpdateChangeFactory.WUPolicyEnforcement` / `DOPolicyEnforcement`
- `AnnoyanceChangeFactory.CopilotDriftEnforcement` / `ProPolicyEnforcement`
  (+ `TierRestrictedSingles` id table)
- Surfaced by: card callout (SettingCardViewModel, 10-3), set-preview notice
  (SetConflictResolver.BuildSkuNotice, 8-4). Never gated by the executor (FR129).
