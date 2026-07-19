# .NET backend solution v1

The backend is one deployable system with inward dependencies:

```text
Longevity.Api -> Longevity.Application -> Longevity.Domain
Longevity.Api -> Longevity.Infrastructure -> Application + Domain
```

- **Domain** owns workflow vocabulary and invariants.
- **Application** owns provider-independent use cases, normalization, deterministic validation/scoring, human-review and publication contracts, and phase orchestration.
- **Infrastructure** owns parameterized PostgreSQL adapters and the optional OpenRouter structured-output adapter.
- **Api** composes configuration, trusted internal endpoints, public read endpoints, health checks, logging, and the background orchestrator.
- **UnitTests** and **Infrastructure.Tests** exercise pure behavior, API boundaries, provider adapters, SQL policy, migration order, and privacy separation without live cloud/model calls.

## Evidence-flow architecture

```text
trusted internal client
  -> intake API (disabled by default, bearer secret, bounded JSON)
  -> private workflow.runs + workflow.source_records
  -> orchestrator claim (row lock + optimistic version)
  -> OpenRouter candidate extraction (strict claim-candidate-v2)
  -> private workflow.claim_candidates
  -> deterministic source-excerpt validation + evidence-scoring-v1 artifact
  -> mandatory human review + append-only approval audit
  -> atomic publisher
  -> public Asset -> Claim -> Source -> Evidence graph
  -> private workflow.publications idempotency/provenance receipt
```

The model proposes structured candidates; it does not publish facts or assign final evidence scores. Deterministic validation checks schema, bounded enumerations, source-excerpt provenance, and scoring inputs. The resulting classification is reproducible software policy output, not a medically validated grade. Explicit human approval remains mandatory, and the publisher reloads and revalidates the approved batch before writing it.

## State, failure, and concurrency model

Runnable transitions are `source_normalized -> extracting`, `candidate_extracted -> validating`, and `approved -> publishing`. Successful phases move to `candidate_extracted`, `awaiting_human_approval`, and `published`. Human review moves an entire eligible batch to `approved` or `rejected`. Extraction with no usable candidate, failed deterministic validation, and exhausted publication retries use explicit terminal states.

Claims use `FOR UPDATE SKIP LOCKED`; completions use expected state and version; transient failures are delayed and bounded by each run's retry count. Intake, review, and publication each use short transactions. Publication also uses advisory transaction locks for idempotency identity and asset slug, plus unique receipt constraints, so identical concurrent retries converge without overwriting existing public assets.

## Security and data separation

PostgreSQL, workflow intake, human review, and orchestration are disabled by default. Enabling intake or review requires PostgreSQL and separate trusted bearer secrets. Enabling orchestration requires PostgreSQL and valid provider configuration. Secrets are host configuration only and are sanitized from responses and logs.

The `workflow` schema has RLS enabled, no anon/authenticated access, and narrow service-role grants. Public educational tables retain read-only anon/authenticated policies. Workflow tables never store personal health/profile data, and pipeline SQL does not query or mutate `private_profile`. Sponsorship remains outside evidence scores.

Migrations are the schema source of truth. This feature adds no dashboard-only schema changes and performs no automatic linking, cloud deployment, production selection, live OpenRouter call, or secret provisioning.

## Validation and deferred operations

Pull-request CI restores, builds Release with warnings as errors, and runs all .NET tests without credentials, a database, Supabase, or a model provider. Local verification additionally covers Debug/Release, frontend type checking/tests/build, repository hygiene, and migration dry-run when the linked project can be positively confirmed as development.

Deferred deliberately: cloud migration approval/deployment, production credentials, telemetry exporters and alerting, source crawling, durable queues, medically validated scoring, and live PostgreSQL concurrency/load testing. A future monitoring-agent integration may consume sanitized workflow state, retry counts, durations, and terminal-failure codes; it must never receive source text, candidate JSON, reviewer notes, secrets, or private-profile data.
