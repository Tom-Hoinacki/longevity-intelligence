# .NET Backend Solution v1

The solution uses direct project references while the backend is one deployable system:

`Longevity.Api` -> `Longevity.Application` -> `Longevity.Domain`
`Longevity.Api` -> `Longevity.Infrastructure` -> `Longevity.Application` and `Longevity.Domain`

- **Domain** holds workflow vocabulary and invariants only.
- **Application** owns provider-independent use-case contracts and phase orchestration.
- **Infrastructure** owns Postgres persistence and the OpenRouter structured-output adapter.
- **Api** composes the host, trusted internal endpoints, health endpoints, logging, configuration, and background orchestrator.
- **UnitTests** covers Domain and Application without live integrations.

## Implemented workflow

```text
trusted source intake
    -> deterministic normalization
source_normalized
    -> extracting (model output)
candidate_extracted
    -> validating (deterministic checks)
awaiting_human_approval
    -> approved
publishing (single atomic public-graph transaction)
    -> published
```

The run repository claims phases with row locking and optimistic version checks. Transient failures are retried; invalid extraction and validation outcomes use explicit terminal states. Publication is keyed by workflow run/version and a SHA-256 content fingerprint, so identical retries are safe and conflicting reuse is rejected.

## Configuration and safety

Postgres, workflow intake, human review, and the orchestrator are disabled by default. Enabling intake or the orchestrator requires Postgres and a trusted secret/provider configuration. Cloud deployment is not part of this implementation; migrations remain the schema source of truth and no production project is selected automatically.

The pipeline keeps private workflow artifacts separate from the public evidence graph. Model output is schema-constrained, deterministic validation is machine-readable, and publication requires explicit human approval. Sponsorship is not combined with evidence scores, and personal health data must not enter the public evidence schema.

## Continuous integration

GitHub Actions runs on pull requests and pushes to `main`. It checks out the repository, installs the .NET 8 SDK, restores the solution, builds Release with warnings treated as errors, and runs all Release tests. The workflow does not contact Supabase, use a database, require credentials, deploy, or call a model provider.

Deferred deliberately: Supabase deployment, production credentials, telemetry exporters, source crawling, queues, and OpenAPI tooling. Real Postgres concurrency integration tests and cloud dry-run validation remain separate operational steps.
