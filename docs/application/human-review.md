# Trusted human-review API and persistence

## Purpose and boundary

Human review is a trusted server-side capability for workflow runs in `awaiting_human_approval`. A reviewer can load the latest validated candidate batch and approve or reject it. A decision appends immutable audit rows and atomically advances the run to `approved` or `rejected`.

The private `workflow` schema is unpublished ingestion state. Browsers and public clients must never connect to it directly or receive a service-role credential. They call this trusted API through a controlled server environment. The API does not provide user management, browser login, organizations, roles, or public evidence publication.

## Configuration and authentication

Human review is disabled by default:

```json
{
  "HumanReview": {
    "Enabled": false
  }
}
```

Enabling it requires all of the following server-side configuration:

- `HumanReview__Enabled=true`
- `HumanReview__AccessSecret` containing at least 32 characters
- `Postgres__Enabled=true`
- `Postgres__ConnectionString` for a trusted server-side PostgreSQL role

No secret value belongs in source control, sample configuration, URLs, logs, traces, or responses. Startup validation fails with a sanitized configuration error when human review is enabled without PostgreSQL or the access secret. PostgreSQL-disabled applications and default development configuration continue to start with no human-review routes.

Requests use a standard bearer header:

```text
Authorization: Bearer <server-configured-secret>
```

The API hashes the supplied and expected values and compares the fixed-size hashes with a fixed-time comparison. Missing or invalid authorization returns `401`. This narrow shared-secret boundary is intended for internal trusted tooling and should later be replaced by the platform's identity/authorization system when one exists.

## Routes

### Load pending review

```text
GET /internal/human-review/{workflowRunId}
```

A successful `200` response contains:

- workflow-run identity;
- expected workflow version;
- current `awaiting_human_approval` state;
- every candidate from the latest extraction version, ordered by ordinal;
- candidate identity, version, and ordinal;
- claim text and structured candidate JSON; and
- deterministic-validation result JSON.

The adapter returns `null` and the endpoint returns `404` when the run does not exist or is not currently pending review. A pending run with no candidates, mixed sources or versions, duplicate/noncontiguous ordinals, invalid JSON, or any candidate not marked `passed` is corrupted state and produces a sanitized `500`; it is never returned as a partial batch.

### Submit a decision

```text
POST /internal/human-review/{workflowRunId}/decisions
```

Request body:

```json
{
  "decisionId": "11111111-1111-1111-1111-111111111111",
  "decision": "approve",
  "reviewerIdentity": "trusted-reviewer-subject",
  "rejectionReason": null,
  "reviewerNote": null
}
```

`decision` is exactly `approve` or `reject`. Rejection requires a non-empty `rejectionReason`. The workflow version and target state are never accepted from the client: the service loads the pending version and derives `approved` or `rejected` server-side. Optional notes are private audit data and are never included in errors or logs.

## Atomic decision transaction

`PostgresHumanReviewPersistence.AppendDecisionAsync` performs one short PostgreSQL transaction:

1. Acquire a transaction-scoped advisory lock derived from the decision identity.
2. Return a previously stored identical decision, or reject conflicting identity reuse.
3. Lock the workflow run row with `FOR UPDATE`.
4. Require `awaiting_human_approval` and the exact expected version.
5. Lock and revalidate the latest candidate batch in ordinal order.
6. Insert one append-only `workflow.approvals` row for every candidate under the shared decision identity.
7. Update the workflow state and increment its version in an optimistic predicate.
8. Commit only after every insert and the transition succeed.

Every exception and request cancellation rolls back with a non-cancelable rollback token. A failed insert or transition therefore cannot leave a partial decision. `approved` remains runnable by the existing orchestrator, which later claims it as `publishing`. `rejected` records `completed_at` and remains terminal. `awaiting_human_approval` is still excluded from automatic claims.

## Optimistic concurrency and idempotency

The GET response exposes the current workflow version for operator visibility, but the POST client cannot submit or override it. The service captures the version from the pending batch, and the transaction requires the same version before changing state. Concurrent reviewers with different decision identities may both reach the row lock, but only the first can satisfy the state/version predicate; the other receives `409`.

Migration `20260712200000_human_review_decision_idempotency.sql` adds:

- trimmed non-empty `decision_identity`;
- nullable legacy-compatible `expected_workflow_version` and `target_state` fields;
- optional immutable `reviewer_note`;
- checks matching approval/rejection to the server-derived target;
- a required rationale for new rejection decisions; and
- a unique `(decision_identity, candidate_id)` constraint.

Existing approval rows are backfilled with their row UUID as a unique decision identity. New batch decisions keep the existing candidate foreign key by writing one approval row per candidate. A database advisory lock serializes all uses of one decision identity, including concurrent first use across service instances.

Before looking for pending work, the service performs a durable decision-identity lookup. Repeating the same identity with identical workflow, decision, reviewer, reason, note, and target returns the original result even after the run has transitioned. Reusing it with different content returns `409`. A new decision identity for an existing non-pending run is also `409`; a truly missing run is `404`.

## Error semantics

| Condition | Status |
| --- | ---: |
| Successful load or decision | `200` |
| Malformed workflow/decision identity, decision, or rejection request | `400` |
| Missing/invalid bearer secret | `401` |
| No matching workflow run | `404` |
| Stale version, conflicting decision identity, concurrent/already-decided batch | `409` |
| Corrupted database state or unexpected persistence failure | sanitized `500` |

Error payloads contain stable codes and generic messages. The endpoint does not return stack traces, SQL, connection strings, credentials, raw exception messages, candidate JSON, rejection reasons, or reviewer notes. Request cancellation is propagated rather than converted into an error response.

## Database security

The migration does not add tables, policies, or public access. `workflow.approvals` remains RLS-enabled with no public policy. `anon`, `authenticated`, and `public` retain no schema/table access. `service_role` retains `SELECT` and `INSERT` only on approvals and has no update/delete/truncate privilege, including on the new audit columns. Existing approval rows are never updated after the migration backfill.

`scripts/validate-data/validate_workflow_schema.sql` verifies the new columns, checks, idempotency index, append-only column privileges, existing RLS/grants, and the rest of the private workflow boundary. The migration must be dry-run before deployment and is not deployed by this change.

## Deferred work

- Platform identity, roles, organizations, and reviewer provisioning.
- Browser-oriented login and CSRF/session handling.
- OpenAPI tooling and a reviewer user interface.
- Live PostgreSQL concurrency integration tests; the normal suite uses contract, SQL-policy, transaction-boundary, API, and configuration tests without Docker or cloud credentials.
- Publication and public evidence projection beyond the existing approved-to-publishing orchestrator transition.
- Deployment of the migration to any Supabase project.
