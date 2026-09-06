# Owner Mode service controls and status

Step 5 connects the existing Settings section to restoration control over IPC. Service process state and restoration consent are separate.

The UI shows Enabled, Paused, Unavailable, or Conflict, plus service state and the reason. Enable succeeds only after the service confirms enabled restoration and consent. Pause remains available when saved consent exists but the service is stopped. Protected external edits are described explicitly.

The existing envelope carries new enable and pause message types. Responses retain nonce and type checks. Older status payloads cannot imply support. Requests have a bounded timeout, and control failures return error envelopes. Capability checks use confirmed restoration status instead of SCM process state.

The injected service graph runs the existing restoration loop with bounded ticks and publishes its results. Tests exercise enable, two scans, one verified write, history, and pause. Production has no trusted restoration graph, so it reports Unavailable and refuses enable. The temporary app-only consent-off recovery from Step 4 remains in place.

## Native activation still required

Step 6 must supply trusted journal/database access, resolve the bound account from trusted baseline storage, and obtain loaded-profile and management evidence. It must replace app-only recovery with shared recovery before registering automatic restoration. Unknown evidence must block writes.

No live Windows setting changed during this batch. Native activation and live evaluation remain untested.

## Verification

The full CI-safe suite and Release build passed in an isolated copy of b4702ea plus this batch. The shared working tree had unrelated Avalonia/Skia package hash mismatches from concurrent hardening work. Isolation used the committed dependency files and did not change the hardening work.

Fresh review passed. Dark/light status screenshots and the real Settings tab were inspected. Settings geometry reads ContentL 25, ContentR 23, ContentT 59 after excluding the complete 43-pixel tab strip. This short page has no scrollbar to measure.
