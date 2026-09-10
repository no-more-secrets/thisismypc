# Owner Mode service controls and status

Software integration is complete. Installed-service live evaluation remains untested.

The unelevated UI shows Enabled, Paused, Unavailable, or Conflict, plus service state and the reason.
Running does not imply consent. Enable uses the elevated Broker, validates the saved owner against the verified UI account,
and requires the service to confirm consent and restoration. Apply a supported Annoyances setting before enabling.

Pause remains available when the service is unavailable. The Broker saves consent-off under the machine lease,
then releases it before stopping the service. A missing service or corrupt restoration journal does not skip that durable opt-out.
Failed consent persistence cannot report success.

The native service graph uses trusted baseline, journal, and history storage with shared recovery.
The catalog contains eleven non-policy Annoyances DWORD targets. Eligibility applies separately to each saved choice.
The bound profile must be loaded; management evidence must establish that the target is unmanaged.
Unknown, managed, unsupported, or untrusted state blocks writes. Interrupted or uncertain attempts disable further restoration.

Read-only IPC exposes service status and restoration history to the UI. Privileged controls retain the existing elevation boundary.
Nonce, type, protocol, and timeout checks remain in place. Older payloads cannot imply restoration support.
Imported restoration records show their outcome and remain excluded from generic undo, redo, and set export.

Protected external edits can be restored regardless of who made them. Pause before changing a protected setting outside ThisIsMyPC.

## Historical Step 5 verification

The full CI-safe suite and Release build passed in an isolated copy of b4702ea plus this batch. The shared working tree had unrelated Avalonia/Skia package hash mismatches from concurrent hardening work. Isolation used the committed dependency files and did not change the hardening work.

Fresh review passed. Dark/light status screenshots and the real Settings tab were inspected. Settings geometry reads ContentL 25, ContentR 23, ContentT 59 after excluding the complete 43-pixel tab strip. This short page has no scrollbar to measure.
