# Evidence Explorer

The Evidence Explorer is the first Next.js product surface for Longevity Intelligence. It makes the public evidence graph navigable as a traceable path:

```text
Asset → Claim → Evidence item → Source
```

## Routes

- `/assets` — bounded public asset registry.
- `/assets/{slug}` — asset summary, registry counts, and linked claims.
- `/claims/{claimId}` — claim text, registry scores, evidence items, limitations, and source provenance.

## Data source and modes

`apps/web/lib/evidence-api.ts` is a server-side typed client for the existing public API:

- `GET /api/v1/assets`
- `GET /api/v1/assets/{slug}`
- `GET /api/v1/claims/{claimId}`

Local development uses the .NET API's explicitly configured `Demo` provider at `http://localhost:5271`. The interface labels this content as illustrative and does not silently replace failed API requests with fixtures. Production requires `EVIDENCE_API_BASE_URL`; `EVIDENCE_API_PROVIDER` controls the provider label shown in the interface and must match the API configuration.

The Explorer never reads the private `workflow` schema and does not use private-profile data. Supabase credentials are not required for the public API Demo flow.

## Relationship to other applications

- `apps/web` is the public web and desktop-browser Explorer.
- `apps/mobile` remains the Expo React Native iOS/Android application foundation; it does not share every web UI component.
- `src/Longevity.Web` is the older standalone Vite dashboard prototype and remains preserved for comparison and migration reference.
- `src/Longevity.Api` remains the backend boundary for public evidence reads.

## Run and validate locally

```text
dotnet run --project src/Longevity.Api --launch-profile http
copy apps\web\.env.example apps\web\.env.local
npm run web:dev
npm run web:test
npm run frontend:type-check
npm run web:build
```

The Explorer is educational evidence navigation, not medical advice. It displays registry values as supplied, leaves unavailable scores unavailable, preserves source limitations, and does not make personalized recommendations or unsupported health claims.

## Current limitations

- The local Demo provider is illustrative interface data, not a production evidence catalog.
- Search, advanced filtering, authentication, and Postgres-backed production browsing remain future work.
- The service worker and PWA shell are minimal and are not an offline evidence cache.
