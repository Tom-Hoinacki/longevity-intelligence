# Longevity.Infrastructure

This project contains future adapters only; it has no active integrations in the initial skeleton.

- Postgres persistence foundation lives here and is disabled by default until a real connection string is supplied.
- The first workflow persistence capability atomically claims only source-normalized, candidate-extracted, and approved runs; a real Postgres concurrency integration test remains deferred.
- The workflow failure-and-retry recorder uses the same optimistic-concurrency foundation; a real Postgres retry integration test remains deferred.
- `Persistence/Postgres`: durable workflow and evidence persistence.
- `AI/OpenAI` and `AI/Anthropic`: provider adapters behind Application contracts.
- `ScientificSources/PubMed` and `ScientificSources/ClinicalTrials`: authoritative-source clients.
- `Telemetry`: observability adapters.

Adapters belong here when they implement an Application contract and can be tested without leaking provider or database details into Domain or Application.
