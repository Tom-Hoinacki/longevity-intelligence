# Human review application boundary

This boundary loads validated candidates awaiting human approval and appends an approval or rejection decision through `IHumanReviewPersistence`. It does not expose HTTP endpoints or perform database work.

Only non-empty, contiguous candidate ordinals from one workflow run and version are eligible. Candidates must have object JSON roots and successful deterministic validation. Approval transitions to `approved`; rejection transitions to `rejected` and requires a reason. Decisions carry optimistic-concurrency versions, timestamps from `TimeProvider`, and unique decision identities for append-only persistence and later idempotency handling.

The application layer deliberately excludes provider prompts and normalized source text. API and Postgres adapters remain deferred.
