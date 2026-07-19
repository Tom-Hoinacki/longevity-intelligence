# OpenRouter claim extraction

This is an optional provider adapter behind `IClaimExtractionModel`. It is registered only when the workflow orchestrator is enabled; both are disabled by default.

Configure `OpenRouterClaimExtraction:BaseAddress`, `ApiKey`, `Model`, `SchemaVersion`, `PromptVersion`, optional `ApplicationTitle` and `ApplicationUrl`, and `RequestTimeout`. Register it explicitly with `AddOpenRouterClaimExtractionModel(configuration)`.

The adapter sends the normalized source in a JSON user payload and requests a strict object containing an ordered `candidates` array. Each candidate has bounded `claimText` and a `claim-candidate-v2` object containing asset identity, source excerpt, evidence direction/level, and deterministic scoring inputs. The response is parsed through the same strict candidate parser used by validation. Final evidence, hype, risk, and verdict scores are not accepted from the model. Provider usage and safe request identifiers are mapped into execution metadata.

The API key and source text are never placed in errors, URLs, logs, or trace identifiers; the API key is sent only in the provider authorization header. Provider response bodies are not exposed. The adapter deliberately performs no retries; retry policy belongs to orchestration. Tests use fake HTTP handlers and make no live OpenRouter request. Credential provisioning and cloud/production enablement remain separate approved operations, and human approval is always required before publication.
