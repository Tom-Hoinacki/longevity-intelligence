# Public evidence read API

The API exposes only the educational `public` evidence graph. It never reads the private `workflow` schema.

Routes:

- `GET /api/v1/assets?page=1&pageSize=20` — deterministic, bounded asset listing.
- `GET /api/v1/assets/{slug}` — asset detail with claims.
- `GET /api/v1/assets/{slug}/claims` — claims for an asset.
- `GET /api/v1/claims/{claimId}` — claim detail.

`PublicEvidence:Provider` selects `Demo` or `Postgres`; it must be explicit. Demo mode is deterministic illustrative data for local frontend development and makes no medical recommendation. Postgres mode requires the existing `Postgres:Enabled` configuration and uses parameterized queries against public tables. There is no silent fallback between providers.

Responses use immutable application DTOs, stable ordering, bounded pagination, and omit unavailable scores rather than manufacturing conclusions. The API does not expose workflow records, unpublished candidates, reviewer metadata, model metadata, credentials, or personal health data.
