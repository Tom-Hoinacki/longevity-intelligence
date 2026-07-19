# Publication application phase

Publication is the provider-independent boundary between an approved private workflow batch and the public evidence graph. It accepts only the latest candidate version whose complete batch passed deterministic validation and received one explicit human approval decision. It cannot introduce claims outside that approved batch.

## Preconditions and evidence provenance

`PublishingWorkflowRunPhaseHandler` requires the run to be claimed in `publishing`, validates its identity and optimistic version, checks approval/reviewer identities and time, requires contiguous unique candidate ordinals, and verifies that each candidate links to the batch's authoritative source. Structured candidate JSON must satisfy `claim-candidate-v2`.

Before opening a transaction, the PostgreSQL adapter reparses each candidate and its passed deterministic validation artifact. The published evidence score and policy ID come from that artifact, never from model-proposed final scores. Its alignment must match the candidate's evidence direction. Supporting-excerpt presence was already checked against private normalized source text during deterministic validation. Candidates are evidence proposals until this entire gate has completed; neither model output nor a deterministic pass is independently a public fact.

The policy produces reproducible software classifications and reason codes. It is not a medically validated grading system, diagnosis, recommendation, or substitute for expert review. Sponsorship data is not accepted by this boundary and cannot affect evidence scoring.

## Atomic mapping

`IEvidencePublicationPersistence` loads an approved batch and publishes one `AtomicPublicationCommand`. One short PostgreSQL transaction writes:

1. one public source;
2. exact-match get-or-create assets;
3. public claims with deterministic evidence score and `evidence_scoring_policy_id`;
4. source-to-claim evidence links; and
5. a private publication receipt containing public source and claim IDs.

Asset identity is serialized by slug. An existing asset is reused only when its name and type agree; model output never overwrites public asset metadata. Any error or cancellation rolls back the complete graph write. The private-profile schema is never read or written.

## Idempotency, concurrency, and retries

The stable idempotency key combines workflow-run identity and version. A SHA-256 fingerprint covers the exact approved content, including deterministic artifacts. An advisory transaction lock serializes that identity before the receipt lookup. An identical retry commits no duplicate graph rows and returns `AlreadyPublishedIdentically`; the same key with a different fingerprint is a conflict. A unique run/version constraint provides an additional database guard.

Provider/model work finishes before publication starts. Failures propagate to orchestration, which either schedules the run for retry or moves it to `publication_failed` after its configured retry limit. Monitoring exporters and live-database concurrency integration tests remain future operational work; static transaction-policy tests and application tests run without credentials or cloud access.

The public evidence graph is educational and must never contain personal health/profile data. No cloud migration, provider request, or production deployment is performed merely by building or testing this feature.
