# Publication application phase

The publishing phase is the provider-independent boundary between an approved workflow batch and the public evidence graph. It accepts only claims that passed deterministic validation, received explicit human approval, and retain provenance to the batch's authoritative source. It cannot introduce claims outside the approved candidate batch.

## Application boundary

`IEvidencePublicationPersistence` has two operations: load the approved batch for a claimed workflow run and publish one complete `AtomicPublicationCommand`. The Postgres adapter writes the idempotency record, source, assets, claims, and evidence links in one short database transaction. Application contracts contain no SQL or provider-specific types. The contract is backed by `20260712210000_workflow_publication_idempotency.sql`.

`PublishingWorkflowRunPhaseHandler` validates workflow identity and version, publishing state, approval identity and time, reviewer identity, claim identities and contiguous ordinals, validation and approval flags, object-root structured JSON, and source evidence links. Invalid batches fail with sanitized invariant messages. Successful new publication and an identical prior publication both advance to `published`; persistence conflicts and failures propagate.

## Idempotency and ordering

The stable idempotency key combines workflow-run identity and version. A SHA-256 content fingerprint distinguishes the exact payload. Repeating an identical batch produces the same key and fingerprint. Reusing the key with different content is a conflict and must not be reported as success by an adapter.

Commands snapshot collections, sort claims by ordinal, and sort evidence links deterministically. Callers cannot mutate the validated command through the original input collections.

## Evidence-graph mapping and safety

The adapter maps `PublicationSource` to the public source record, structured candidate fields to assets/claims/evidence, and `PublicationEvidenceLink` to source-to-claim provenance edges. Sponsorship remains separate from evidence scoring. The boundary does not provide medical advice and must not receive or publish personal health data.

Repeated publication with the same run/version and fingerprint is a no-op; reuse with a different fingerprint is a conflict. Model/network work is completed before the transaction begins.
