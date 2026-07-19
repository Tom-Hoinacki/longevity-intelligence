# Workflow intake

When enabled, `POST /internal/workflow-runs` accepts a trusted source submission and creates a private workflow run in `source_normalized`. The endpoint is disabled by default, requires PostgreSQL, and uses a bearer secret from configuration. Secrets must come from the host environment; do not commit them.

The intake service normalizes source text and authoritative identity before persistence. Identity priority is DOI, PMID, ClinicalTrials.gov identifier, then canonical URL. The normalized content hash is part of the idempotency comparison. Repeating the same workflow type and idempotency key with the same identity and content returns the existing run; reusing that key for different content returns `409`.

The orchestrator is also disabled by default. When enabled, it claims runnable runs with optimistic concurrency, calls the configured OpenRouter structured-output adapter, persists candidate artifacts, runs deterministic validation, waits for human approval, and publishes only approved candidates atomically into the public evidence graph.
