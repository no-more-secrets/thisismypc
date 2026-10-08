# Network & Firewall

The first implementation provides Connections, DNS, and Firewall tabs under System.

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
- GitNexus change analysis reports high risk around shared Broker wiring. Its stale index omits new symbols; index refresh failed during worker startup.

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
