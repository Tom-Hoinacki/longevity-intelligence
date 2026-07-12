# Workflow Schema v1

`workflow` is a private schema for unpublished AI evidence-ingestion work. It is intentionally separate from the public educational evidence graph.

## Tables and relationships

- `workflow.runs` is the durable orchestration record. Its state, retry fields, availability timestamp, and version support one orchestrator executing separate phases safely.
- `workflow.source_records` stores the normalized authoritative input for a run. It retains normalized text directly in Postgres for version 1 so extraction is reproducible without object storage. `source_identity_key` is deterministically derived from DOI, then PMID, then ClinicalTrials.gov identifier, then canonical URL. Its unique identity, content-hash, and normalization-version combination permits a new record when source content or normalization changes.
- `workflow.claim_candidates` stores structured, versioned model output and deterministic validation results. `candidate_version` identifies one extraction-output version for a source, while `candidate_ordinal` identifies an individual claim within that extraction version. Ordinals begin at `1`; multiple candidates may share a source and extraction version when their ordinals differ. Re-extraction creates a new `candidate_version` and does not overwrite an earlier version. Proposed scores are constrained to 0 through 5 but are candidates, not scientific certainty.
- `workflow.approvals` is append-only human decision history. Multiple decisions are intentional; the current decision is ordered by `created_at`, then `id`. An approval is required before future publication logic writes to `public.sources`, `public.claims`, and `public.claim_evidence`.

Foreign keys use `on delete restrict`: workflow records are audit material and must not disappear through an incidental cascade.

## Security boundary

The migration revokes schema, table, and sequence access from `anon`, `authenticated`, and `public`; enables RLS with no public policies; and grants trusted server-side access only to `service_role` among application roles. The `workflow` schema must not be added to Supabase public API schemas. The service-role credential is server-only and must never appear in browser code, mobile code, commits, logs, or `.env` files checked into Git. Database owners and elevated administrators retain maintenance capabilities and are outside the application-role append-only boundary.

## Reads and writes

The .NET orchestrator may create and update runs. Source records are insert-only for `service_role`; corrections create a new source record or normalized version. Candidates are insert-only except for column-limited updates to `deterministic_validation_status` and `deterministic_validation_result`; corrections create a new candidate version. `service_role` can insert and read approvals but cannot update or delete them. Human-review tooling will call trusted server-side endpoints; browsers never access this schema directly. Public clients continue reading only the existing public evidence tables.

The BackgroundService may automatically claim `received`, `source_normalized`, `extracting`, `candidate_extracted`, `validating`, `approved`, and `publishing` runs only when `retry_count < max_retries`. `awaiting_human_approval` is deliberately excluded. `published`, `no_candidate_extracted`, `validation_failed`, `rejected`, and `publication_failed` are terminal until a trusted operator explicitly reschedules work by changing state and availability.

Default privileges are created for the database role that executes this migration; PostgreSQL default ACLs are creator-role-specific. They grant future workflow tables only `SELECT` and `INSERT` to `service_role`, and future sequences only `USAGE` and `SELECT`. Every future workflow migration must explicitly review object ownership and grants before relying on these defaults.

## Deferred deliberately

This migration does not add workflow transition procedures, publication logic, queues, vectors, embeddings, object storage, source crawling, a user/organization model, or personal health data.
