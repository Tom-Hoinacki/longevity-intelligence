# Workflow intake and controlled evidence pipeline

`POST /internal/workflow-runs` is a trusted, internal-only entry point for one authoritative scientific source. The route is absent unless `WorkflowIntake:Enabled=true`; enabling it also requires PostgreSQL and a bearer secret of at least 32 characters. Secrets must come from host configuration. They must never be committed, returned by the API, or written to logs.

The endpoint accepts JSON containing `idempotencyKey`, `sourceType`, `title`, `rawContent`, and optional canonical identifiers. It rejects unknown JSON properties, malformed or oversized requests, titles longer than 500 characters, content longer than 1,000,000 characters, non-HTTP(S) canonical URLs, and unsupported source types. Supported types are `journal_article`, `preprint`, `clinical_trial`, `systematic_review`, and `meta_analysis`. The workflow type is fixed by the server; clients cannot select another pipeline.

Responses are `201` for a new run, `200` for an identical retry, `400` for invalid input, `401` for missing or invalid authorization, `409` when an idempotency key is reused for different content, and `503` for unavailable persistence. Error bodies are sanitized and never echo submitted source content or database/provider details. Request cancellation propagates to persistence.

## Identity and idempotency

The intake service deterministically normalizes the source before persistence. Identity priority is DOI, PMID, ClinicalTrials.gov identifier, then canonical URL. A lowercase SHA-256 content hash covers the normalization version, normalized source type, title, identity, canonical URL, and normalized text. A repeated workflow type and idempotency key returns the existing run only when both identity and content still match; conflicting reuse is rejected. Run and source creation occur in one transaction.

## Controlled progression

The orchestrator is separately disabled by default. When enabled, it claims only runnable states with row locking and optimistic versions:

```text
trusted source -> normalized private source
               -> schema-constrained model candidates
               -> deterministic source/provenance validation and scoring
               -> explicit human approval or rejection
               -> one atomic public evidence-graph publication
```

Model candidates are proposals, not established facts. The model cannot assign the published evidence score. Every candidate must include a supporting excerpt that exactly occurs in the normalized source and bounded inputs used by the repository's deterministic `evidence-scoring-v1` policy. Validation emits a machine-readable artifact containing the policy identity, score, classification, alignment, and reason codes.

Human review is a mandatory gate. Validation success does not approve a candidate, and a reviewer cannot approve a candidate that failed validation. Publication reloads the approved candidate version and deterministic artifact, checks provenance again, and writes only that approved batch.

## Failure and retry behavior

Phase work is claimed with a workflow version. A stale completion or failure update becomes a conflict instead of overwriting newer state. Transient extraction, validation, and publication failures are rescheduled until the run's retry limit is reached; terminal states distinguish `validation_failed` and `publication_failed`. No model or network call occurs inside a publication transaction. Identical publication retries are no-ops, while conflicting reuse is rejected.

This pipeline is educational evidence infrastructure. It does not give medical advice, its deterministic score is not a medically validated grade, and it must not ingest personal health/profile data. The private-profile subsystem is not queried or written by intake, extraction, validation, review, or publication.
