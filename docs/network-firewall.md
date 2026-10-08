# Network & Firewall

The first implementation provides Connections, DNS, and Firewall tabs under System.

## Browsing and editing

Sam's October 8 interaction rule: switching back to the captured state cancels the staged change. Switches remain editable while staged.
Do not add separate discard buttons to switch rows. The shared queue still provides Discard for the entire batch.
DNS can replace its staged value or revert it inside the editor. Unchanged DNS rows have no revert button.

Connections and DNS default to connected adapters with assigned IP addresses, labeled In use.
The filter also offers Connected, Disconnected, Not present, and All adapters. Search covers every adapter regardless of the selected filter.
Compact rows expand for details or DNS editing. Filtering never removes pending changes.

Firewall browsing groups rules by full application path, service, or rule name. Unresolved package resources share their package-name group.
Applications, Windows, and Other separate the browsing lists. Windows classification uses the Windows directory, Windows path variables, or Microsoft package identifiers.
These categories are browsing hints, not verified publisher identities. Service-only rules remain under Other unless another field identifies their category.
Search covers every category, including ports and addresses. Matching groups expand to show individual rule names.
Both group lists and rule lists use virtualized rows. Original rule names and full paths remain available in the expanded details.

## Available controls

- Connections shows adapter identity, link state, link speed, addresses, gateway, and hardware address. The switch changes administrative traffic state through IP Helper.
- DNS shows reported servers and edits the static IPv4 server list. Blank input restores automatic configuration. IPv6 and encryption settings are separate.
- Firewall shows Domain, Private, and Public profiles, their active state, and default traffic behavior. Each editable profile has an enable switch.
- Firewall rules are searchable and read-only. Details include application, service, direction, action, protocol, ports, addresses, and profile scope.

Adapter switches do not remove devices or drivers. Connections with unsupported or unreadable state remain visible with editing disabled.
DNS editing is blocked when detected policy or profile-specific settings could override the value. Unsupported server formats also disable editing.
Firewall profiles with detected policy restrictions cannot be edited. These checks are conservative, not a complete management-provenance assessment.
Windows resource names are resolved when available. Unresolved rule names remain visible as their original identifiers.

## Changes and undo

Every edit uses the shared pending-changes pipeline and captures its previous value before staging.
The Broker accepts only adapter GUIDs, DNS GUIDs, or the three known firewall profile IDs with matching locations and validated values.
Writers reject changes when the current value differs from the captured value. Undo and rollback use the same validation with inverted descriptors.
Disabling an adapter interrupts its connections. Disabling a firewall profile removes its Windows Firewall protection.
Native API success is reported after each write; elevated acceptance testing remains pending.

IP Helper provides adapter state and interface DNS settings. Windows Firewall uses raw COM interfaces compatible with NativeAOT.
Core contains only data contracts and interfaces. Native access remains in the Interop projects.

## Verification

- The CI-safe suite passed 3,076 tests. Focused tests cover descriptor rejection, DNS validation, simulated undo and rollback, queue behavior, and independent scan failures.
- Screenshots cover all tabs, both themes, narrower windows, expanded profiles, staging, invalid input, and unavailable state.
- Full-window screenshots pass the shared edge measurements: left 25, right 23, content top 59, scrollbar lane 10.
- A read-only NativeAOT executable read 43 adapters, three firewall profiles, and 1,074 firewall rules on the development host.
- Fresh-context review found no remaining blockers after queue synchronization and profile scrolling fixes.
- October 8: the graph index refreshed successfully. Change analysis reports low risk for the compact network UI changes.

October 8 verification passed 3,078 CI-safe tests and seven focused network tests, including read-only full-window screenshots.
Mouse tests cover switching back before apply and after a simulated apply. A 1,100-rule fixture checks grouping and search across categories.
The development host shows three adapters in the default view, out of 43 available adapters.

Live elevated changes, actual undo, organization-managed machines, and the signed release build remain unverified.
No live network or firewall settings changed during development.

### Pending acceptance on a test PC

1. Open System, then Network & Firewall.
2. Open Connections and choose an unused adapter.
3. Change its Enable switch.
4. Apply the pending change.
5. Confirm the reported state after refreshing the page.
6. Undo the change from History.
7. Confirm the previous state after refreshing the page.
8. Repeat with a DNS change on a test connection.
9. Confirm that Undo restores the previous automatic or manual DNS setting.
10. Test a firewall profile change on an isolated test PC.
11. Confirm that Undo restores the previous profile state.

## Deferred scope

These tabs are an initial implementation, not complete network administration.
Wi-Fi connection management, static IP settings, IPv6 DNS, DoH, hosts editing, and adapter power settings remain pending.
Firewall rule creation, editing, removal, import, and export need a separate model that preserves every supported rule field for undo.
Traffic, VPN & Proxy, Sharing, and Diagnostics remain future tabs.
