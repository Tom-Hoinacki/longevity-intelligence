# OpenRouter claim extraction

This is an optional provider adapter behind `IClaimExtractionModel`; it is not wired into the running API.

Configure `OpenRouterClaimExtraction:BaseAddress`, `ApiKey`, `Model`, `SchemaVersion`, `PromptVersion`, optional `ApplicationTitle` and `ApplicationUrl`, and `RequestTimeout`. Register it explicitly with `AddOpenRouterClaimExtractionModel(configuration)`.

The adapter sends the normalized source in a JSON user payload and requests a strict object containing an ordered `candidates` array. Each candidate has non-empty `claimText` and an object `structuredCandidate`. Provider usage and safe request identifiers are mapped into execution metadata.

The API key and source text are never placed in errors, URLs, headers, logs, or trace identifiers. Provider response bodies are not exposed. The adapter deliberately performs no retries; retry policy belongs to orchestration. Production integration, credential provisioning, and human approval remain deferred.
