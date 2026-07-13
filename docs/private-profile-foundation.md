# Private profile foundation

The private profile foundation is the first private data plane for personalized Longevity Intelligence. It is deliberately isolated from the public evidence catalog and workflow schema so personal measurements, lab observations, preferences, goals, and consent events cannot become public educational records by an accidental join or fixture.

## Boundary and data flow

Authenticated subject → `ICurrentUserContext` → application service → parameterized Postgres store → `private_profile` schema

The API never accepts a profile ID to determine ownership. The subject comes from the authenticated principal (`sub`, with `NameIdentifier` as a compatibility fallback). Each private record is linked to one profile through a foreign key. The Postgres adapter also sets a transaction-local `app.current_user_subject` value and the migration adds profile-scoped RLS policies.

The current application host does not configure a production identity provider yet. Private routes still carry authorization metadata and reject unauthenticated or subjectless requests. A real authentication scheme must be wired before exposing these routes to users; there is no impersonating request header.

## Domain model

- `profiles` represents a person independently from an identity provider. Birth date and birth year are mutually exclusive; sex at birth and gender are separate fields.
- `consents` is append-only. The latest event for a consent type is the effective state, while prior policy versions and withdrawals remain available for audit. The five categories are profile data storage, personalized analysis, research use, de-identified aggregate-data use, and commercial partner matching.
- `body_measurements` is append-only and preserves the entered numeric value and unit. Known types include weight, height, waist circumference, and body-fat percentage; the text type field supports future types without a new table.
- `lab_observations` is append-only and accepts either a numeric value or a text value. Standardized code systems and codes are optional so future LOINC support does not make imported or self-reported records invalid.
- `preferences` has one current row per profile and uses constrained values for risk, evidence confidence, cost-versus-confidence, currency, and insurance preference.
- `goals` supports multiple active or inactive goals, including a user-defined type, without making treatment or product recommendations.

Creating or loading a profile creates explicit declined default consent events for all five categories. Optional research, aggregate-data, and commercial-partner consent therefore fail closed. Recording a grant or withdrawal adds another event; it does not overwrite the prior event.

## API

All routes are under `/api/v1/me` and require authorization:

- `GET` / `PUT` `/profile`
- `GET` / `POST` `/consents`
- `POST` `/consents/{consentType}/withdraw`
- `GET` / `POST` `/measurements`
- `GET` / `POST` `/labs`
- `GET` / `PUT` `/preferences`
- `GET` / `POST` `/goals`
- `PATCH` `/goals/{goalId}`

Measurement and lab collection endpoints use bounded cursor pagination (`limit` 1–100) with deterministic ordering by observed timestamp and observation ID. Request and response records are immutable DTOs; database entities are not exposed. Endpoint errors use stable generic responses and never include exception details or private values.

## Configuration and migrations

Postgres is controlled by the existing `Postgres` configuration section:

```json
{
  "Postgres": {
    "Enabled": true,
    "ConnectionString": "<development-only connection string>"
  }
}
```

The migration `20260713000000_private_profile_foundation.sql` is the schema source of truth. The application does not create or alter tables at startup. When Postgres is disabled, the registered store returns an unavailable result and the private API returns `503`; it does not use a demo or in-memory production fallback.

The migration enables and forces RLS, adds ownership policies, foreign keys, checks, and indexes for profile/timestamp access. Append-only tables are granted select/insert access to the application roles, while profile, preference, and goal updates are separately granted. No cloud deployment is part of this change.

## Export and deletion boundary

The schema makes a later profile export straightforward: select the profile, then its consents, measurements, labs, preferences, and goals by `profile_id` in a single controlled service operation. Child records use deliberate `on delete cascade` foreign keys so a future deletion workflow can delete the profile root after authorization and audit checks. Export and deletion endpoints, retention policies, legal holds, and recovery procedures are intentionally not implemented here.

## Intentionally not implemented

This foundation does not provide diagnosis, medical advice, treatment prescriptions, recommendations, stack optimization, product catalog or commerce functions, insurance claims, data licensing, document upload/OCR, wearable or health-platform integrations, mobile screens, research sharing, commercial sharing, or aggregate dataset release.

## Compliance limitations

This implementation alone does not claim HIPAA, GDPR, or any other regulatory compliance. Remaining work includes identity-provider and session controls, operational access governance, encryption and key-management review, audit-log retention and tamper protection, backup/deletion semantics, retention and consent-policy workflows, incident response, data-subject rights, vendor agreements, regional processing controls, threat modeling, and an independent legal/security review.

The public evidence schema remains educational and free of personal health information. Tests use only obviously fictional subjects and values.

## Future use

The normalized private profile can later be consumed by a personalization engine through application interfaces rather than HTTP objects or direct controller queries. That creates a stable boundary for evidence views, budget-aware comparisons, outcome tracking, clinician/researcher workflows, and consent-gated aggregate analytics without mixing those capabilities into the initial private data plane.
