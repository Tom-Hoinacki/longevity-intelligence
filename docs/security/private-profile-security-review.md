# Private profile security and migration review

Review date: 2026-07-12

Scope: pull request #9, `feature/private-profile-foundation`

Starting commit: `f05af925df1922415ba804f88c2058abacb8390c`

## Review conclusion

The review selected a backend-only database model. Private HTTP ownership is derived from one validated principal subject, and private database access is mediated by a dedicated `NOLOGIN` role activated only inside a transaction. Supabase browser roles have no private-schema privileges. The implementation remains fail-closed until a real production identity provider and a compliant database runtime login are configured.

## Findings and disposition

| ID | Severity | Finding and evidence | Disposition and verification |
|---|---|---|---|
| PP-01 | High | The migration granted schema/table access to `authenticated` and `service_role`, while RLS policies applied `TO PUBLIC`. This mixed direct Supabase access with backend-mediated access. | Fixed. Policies and grants now target only `private_profile_api`; `PUBLIC`, `anon`, `authenticated`, and `service_role` are revoked, including default privileges. Static migration tests enforce the boundary. |
| PP-02 | High | The store set a subject but did not prove that its pooled connection was a non-owner, non-superuser, non-`BYPASSRLS` login. Such a login could bypass RLS. | Fixed. Each transaction validates session and boundary-role attributes and ownership, requires `NOINHERIT` membership, then uses `SET LOCAL ROLE`. Misconfiguration fails with a sanitized `503`. |
| PP-03 | High | `current_subject()` accepted either a backend custom setting or `request.jwt.claim.sub`, creating two identity assertion paths. | Fixed. The function accepts only the backend transaction-local setting. The direct JWT path and browser-role grants were removed. |
| PP-04 | Medium | The private routes required authorization metadata but the host had no authentication scheme; malformed and duplicate subject claims were not rejected consistently. | Fixed. A reject-all fallback produces a clean challenge until a real scheme is configured. A named owner policy and shared subject validator reject missing, duplicate, padded, control-character, and oversized values. Explicit external defaults are preserved. |
| PP-05 | Medium | Function execution remained available through PostgreSQL's default `PUBLIC` function privilege. | Fixed. Existing and future function privileges are explicitly revoked from public/browser roles and granted only to the boundary role. |
| PP-06 | Medium | `CREATE SCHEMA IF NOT EXISTS` could silently adopt a pre-existing object with unknown ownership or grants. | Fixed. Schema and role creation now fail on collisions and require investigation. |
| PP-07 | Medium | Default consent initialization used `NOT EXISTS` without a uniqueness guarantee, allowing duplicate defaults during concurrent initialization. | Fixed. A partial unique index, reserved-source constraint, and `ON CONFLICT DO NOTHING` make initialization idempotent. Explicit consent writes serialize on the profile row. |
| PP-08 | Medium | Several application checks did not match database precision or structural constraints; future observations, invalid time zones, incompatible known units, and reserved consent provenance were accepted. | Fixed. Application validation and database checks now align on supported structural boundaries. No clinical interpretation was added. |
| PP-09 | Medium | Request size was unbounded and private operations had no deliberately privacy-safe operational signal. | Fixed. Private bodies are limited to 64 KiB. Middleware logs only method, status, and elapsed time, with tests proving paths and fictional private values are omitted. |
| PP-10 | Low | A broad `InvalidOperationException` catch could misclassify unrelated faults as authorization failures. | Fixed. Authorization uses a dedicated exception; unexpected failures remain sanitized server errors. |
| PP-11 | Low | Caller-submitted consent events have no idempotency key, so client retries can append duplicate user events even though defaults are idempotent. | Deferred. Events remain append-only and auditable; introduce an explicit request identity and uniqueness contract before external clients rely on retry safety. |
| PP-12 | Informational | PostgreSQL default privileges apply only to objects created by the role whose defaults were altered. | Documented. Future migrations created by a different owner must repeat explicit privilege revocation and review. |

## RLS and role conclusions

RLS is enabled and forced on all six private tables. Every policy compares the row's owning profile to `private_profile.current_subject()` and applies only to `private_profile_api`. Foreign-key child access is owner-scoped through `profiles`, whose external subject is unique and indexed. The application additionally includes owner predicates in its SQL; those predicates are defense in depth, not a substitute for RLS.

The database owner, migration administrator, and any superuser remain administrative trust principals. PostgreSQL cannot protect data from a fully privileged database administrator. Production operations must restrict and audit those identities.

## Pooling, errors, and cancellation

Role activation and subject state are transaction-local. Commit and rollback clear both before a connection returns to the pool. The store rolls back with a non-request cancellation token so a canceled HTTP request does not abandon cleanup. Static regression tests verify ordering and cleanup code. A live PostgreSQL pool-reuse test remains a deployment prerequisite because no local PostgreSQL engine was available during this review.

Error payloads use generic codes and never include exception text. The private middleware and store emit only fixed operational categories, HTTP method/status/duration, and no exception objects or user data. Infrastructure outside this application is not covered by that guarantee.

## Migration validation boundary

The review performs source assertions for role attributes, grants, revokes, policies, constraints, indexes, collision behavior, transaction-local context, and default-consent idempotency. `scripts/validate-data/validate_private_profile_schema.sql` provides a catalog-only post-migration check without reading or writing private rows. `scripts/validate-data/validate_private_profile_rls.sql` uses fictional rows in a rolled-back transaction to verify two-subject read/write isolation and default-consent uniqueness. The repository build and test suites are also run. A disposable live PostgreSQL/Supabase dry run is still required before deployment; no linked or cloud project was selected or changed in this review.

## Residual production prerequisites

- Real identity-provider configuration and token validation.
- Dedicated `NOINHERIT` runtime login provisioned outside source control and granted only membership in `private_profile_api`.
- Disposable-database migration dry run and two-subject RLS/pooling tests.
- TLS, secret rotation, access review, backup, retention, deletion, audit, rate-limit, incident-response, and log-governance controls.
- CSRF protection if a future authentication scheme uses ambient cookies.
- Idempotency design for caller-submitted consent events.
