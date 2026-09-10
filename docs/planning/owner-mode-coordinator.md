# Owner Mode mutation coordinator

Historical foundation record. Statements about unwired production code describe the original checkpoint.
Current software integration and remaining live evaluation are recorded in the
[single-user plan](owner-mode-single-user-plan.md).

`MutationCoordinator` is a pure Core foundation. This batch does not register it or change App or Service routing.

One `RunAsync` call acquires one lease and awaits recovery on every acquisition, including clean acquisitions.
Recovery returns `OperationResult<bool>`. Only success with value true authorizes the coordinator to call `MarkRecovered`.
The operation receives that same held lease and cancellation token. It must include persistence and rollback within its awaited lifetime.
The coordinator releases the lease before returning or propagating callback exceptions.

`MutationRunResult<T>` separates acquisition, recovery, and operation results. Acquisition and recovery failures retain their original diagnostic objects.
`OperationRan` distinguishes a blocked operation from a default-valued operation result. It does not claim the operation itself succeeded.
The acquisition's lease reference is diagnostic after return and is already disposed.
Callback exceptions propagate unchanged. If release also throws, an aggregate preserves both exceptions.

Nested calls for the same lease name fail before acquisition, including calls through another coordinator instance.
The guard uses async execution context. Independent operations still use the provider for exclusion.
Callbacks must not suppress execution context to bypass nesting checks, acquire the same lease independently, dispose the lease, or launch unawaited writes.
This is a trusted caller contract, not a security boundary against arbitrary Core callers.

Cancellation before recovery clearance cannot authorize the operation. Acquisition cancellation retains the provider's cancellation result.
Cancellation inside recovery or operation propagates and releases the lease.
After an operation starts, the coordinator does not replace a completed result merely because cancellation arrived.
Operations must observe cancellation only at safe boundaries and retain the lease through outcome persistence.

Future routing must use this coordinator once around the whole reversible batch, including rollback, history, and baseline persistence.
History undo and redo each need their own outer operation. Internal delegates must use the supplied lease without nested acquisition.
Recovery policy, trusted storage, durable consent routing, stale-before-state validation, and App crash recovery remain separate prerequisites.
Tests use fake leases only. No kernel mutex, filesystem mutation, or production registration is added by this batch.
