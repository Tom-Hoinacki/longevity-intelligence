# Private profile foundation

The private profile foundation is an isolated data plane for personalized Longevity Intelligence. Personal measurements, lab observations, preferences, goals, and consent events live in the `private_profile` schema; they are not public evidence records and must not be copied into the public educational schema.

## Trust boundary and ownership

The selected access model is backend-only:

Authenticated principal -> ASP.NET Core owner policy -> application service -> parameterized Postgres store -> transaction-local database role and subject -> `private_profile` RLS

The API never accepts a profile or subject identifier to establish ownership. It accepts one authoritative `sub` claim, with `ClaimTypes.NameIdentifier` supported only when an identity provider maps its immutable subject there and no `sub` claim is present. Missing, duplicate, whitespace-padded, control-character, or oversized subjects fail authorization.

No production identity provider is configured in this foundation. The host registers a reject-all fallback, so the private routes return an authentication challenge until a real scheme is explicitly configured. The test-only `X-Test-Subject` handler exists only in the test assembly and is not referenced by production code.

## Database access model

The migration creates `private_profile_api` as `NOLOGIN`, `NOSUPERUSER`, `NOCREATEDB`, `NOCREATEROLE`, `NOINHERIT`, and `NOBYPASSRLS`. It is the only role with schema, table, and `current_subject()` privileges, and every RLS policy targets that role. `PUBLIC`, `anon`, `authenticated`, and `service_role` are explicitly revoked. Schema-scoped defaults protect future private tables and sequences. Because PostgreSQL grants function execution to `PUBLIC` globally by default, the migration also removes that default for all future functions created by the migration role; individual public functions must be granted deliberately. Default privileges are creator-role-specific, so every future migration using a different object owner must repeat this review. Browser clients therefore have no direct access to this schema.

Provisioning must create a separate runtime login outside the repository and grant it membership in `private_profile_api`. The runtime login must be `NOINHERIT`, must not own the schema or its relations, and must not be a superuser, `BYPASSRLS`, `CREATEROLE`, or `CREATEDB`. Its secret belongs in the deployment secret store, never in source control. Configure `Postgres.ConnectionString` to use that login.

Every store operation:

1. Opens a connection and transaction.
2. Verifies the pooled session has no leaked role/subject state, both the session login and boundary role remain least-privilege, and the login can explicitly assume `private_profile_api`.
3. Executes `SET LOCAL ROLE private_profile_api`.
4. sets `app.current_user_subject` with `set_config(..., true)`.
5. Executes only parameterized owner-scoped SQL and commits, or rolls back on failure or cancellation.

`SET LOCAL` and the transaction-local setting reset at commit or rollback, including when a physical connection returns to a pool. The subject function deliberately has no JWT-setting fallback. A holder of the trusted backend database credential can assert a subject, so credential protection, connection-string access, and operational database access remain part of the security boundary.

## Data and consent semantics

- `profiles` stores birth date or birth year, never both, with separate sex-at-birth and gender fields.
- `consents` is append-only. Five explicit declined events are initialized by default. A partial unique index and conflict-safe insert prevent duplicate system defaults under concurrent profile initialization. The reserved `system_default` source cannot be submitted by callers. Explicit consent writes lock the profile row before insertion so writes for one profile are serialized.
- `body_measurements` is append-only and preserves the submitted numeric value and canonical unit. Known measurement types enforce compatible units; future types remain extensible.
- `lab_observations` is append-only and accepts exactly one numeric or text value. Input lengths, numeric precision, source values, and abnormal flags are bounded.
- `preferences` has one current row per profile.
- `goals` supports multiple bounded, owner-scoped goals without making health or product recommendations.

Birth fields, time zones, numeric ranges, known measurement units, future observation skew, source labels, and consent provenance are validated before persistence. Database constraints provide a second structural boundary. These checks validate data shape only; they do not interpret a result or make a medical claim.

## API and operational behavior

All routes are under `/api/v1/me` and use the `PrivateProfileOwner` policy:

- `GET` / `PUT` `/profile`
- `GET` / `POST` `/consents`
- `POST` `/consents/{consentType}/withdraw`
- `GET` / `POST` `/measurements`
- `GET` / `POST` `/labs`
- `GET` / `PUT` `/preferences`
- `GET` / `POST` `/goals`
- `PATCH` `/goals/{goalId}`

Measurement and lab lists use bounded cursor pagination with deterministic timestamp/UUID ordering. Request and response records are immutable DTOs and responses never expose the external subject. Private request bodies are limited to 64 KiB. Error responses are stable and omit exception details and submitted values.

The private observability middleware records only HTTP method, status code, and elapsed time. Database security-context and rollback failures emit fixed diagnostic categories without exception objects or connection/user details. These logs do not record paths, query strings, subjects, headers, bodies, exception messages, measurements, or lab values. Deployment-level proxy, APM, and database logging still require a separate privacy review.

## Migration and deployment prerequisites

`supabase/migrations/20260713000000_private_profile_foundation.sql` is the schema source of truth. It intentionally fails if the schema or boundary role already exists so an unexpected object cannot be silently adopted. The application does not create or alter database objects at startup. No cloud migration is part of this change.

Before production exposure:

- configure and test a real token-validating identity provider, including issuer, audience, signing keys, expiry, replay/session policy, and immutable subject mapping;
- use a dedicated `NOINHERIT` runtime login and verify the startup access checks pass;
- dry-run the migration against a disposable database, then obtain human approval before any linked project deployment;
- run `scripts/validate-data/validate_private_profile_schema.sql` as a migration administrator on that disposable database;
- run `scripts/validate-data/validate_private_profile_rls.sql`; it uses fictional rows inside a rolled-back transaction to test two-subject isolation and default-consent uniqueness;
- test RLS with two real database subjects and test commit, rollback, cancellation, and pooled-connection reuse;
- review TLS, secret rotation, backups, deletion/retention, audit access, incident response, rate limiting, and any cookie-authentication CSRF controls;
- review proxy, APM, database, and support logs for private-data leakage.

When Postgres is disabled, the private store returns `503`; there is no demo or in-memory production fallback.

## Export, deletion, and compliance boundary

The foreign-key structure supports a future authorized export and a root-profile deletion with cascading children. Export and deletion endpoints, retention schedules, legal holds, recovery procedures, audit-event retention, and idempotency keys for caller-submitted consent events are not implemented.

This foundation does not claim HIPAA, GDPR, or other regulatory compliance. It does not provide diagnosis, medical advice, treatment, recommendations, commerce, insurance claims, research sharing, commercial sharing, document upload/OCR, wearable integration, or aggregate dataset release.
