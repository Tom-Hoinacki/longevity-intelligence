# .NET Backend Solution v1

The solution uses direct project references while the backend is one deployable system:

`Longevity.Api` -> `Longevity.Application` -> `Longevity.Domain`
`Longevity.Api` -> `Longevity.Infrastructure` -> `Longevity.Application` and `Longevity.Domain`

- **Domain** holds workflow vocabulary and invariants only.
- **Application** owns provider-independent use-case contracts.
- **Infrastructure** is the home for Postgres persistence foundations and the future model-provider, telemetry, and scientific-source adapters.
- **Api** composes the host, health endpoints, logging, configuration, and orchestrator shell.
- **UnitTests** covers Domain and Application without live integrations.

## Continuous integration

GitHub Actions runs on pull requests and pushes to `main`. It checks out the repository, installs the .NET 8 SDK, restores the solution, builds Release with warnings treated as errors, and runs all Release tests. The workflow does not contact Supabase, use a database, require credentials, deploy, or call a model provider.

## Implemented workflow phases

The implemented application and Postgres boundaries are:

```text
source_normalized
    ↓ claim
extracting
    ↓ extract and persist
candidate_extracted
    ↓ claim
validating
    ↓ validate and persist
awaiting_human_approval
```

Expected terminal paths are `extracting → no_candidate_extracted` and `validating → validation_failed`.

Direct references keep contracts simple and changes visible while these projects evolve together. An adapter becomes a separate class-library project when it needs independent lifecycle, optional deployment, distinct ownership, or isolation from other infrastructure concerns. A private NuGet package is justified only after a stable, versioned contract is consumed by more than one independently released solution; it is not useful merely to separate folders.

Deferred deliberately: concrete model-provider adapter, real deterministic scientific validation rules, human-review API, publishing handler, public evidence publication, complete processor dependency-injection registration, Supabase deployment, production credentials, telemetry exporters, source intake, source normalization implementation, authentication, queues, and OpenAPI tooling. Postgres persistence is disabled by default. The first repository operation atomically claims supported workflow phases; real Postgres concurrency integration tests remain deferred. The orchestrator is disabled by default; enabling it without a real `IWorkflowRunProcessor` fails clearly rather than pretending workflow processing exists.
