# Longevity.Infrastructure

This project contains future adapters only; it has no active integrations in the initial skeleton.

- `Persistence/Postgres`: durable workflow and evidence persistence.
- `AI/OpenAI` and `AI/Anthropic`: provider adapters behind Application contracts.
- `ScientificSources/PubMed` and `ScientificSources/ClinicalTrials`: authoritative-source clients.
- `Telemetry`: observability adapters.

Adapters belong here when they implement an Application contract and can be tested without leaking provider or database details into Domain or Application.
