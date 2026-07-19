# Cross-platform foundation

The first frontend milestone uses one TypeScript workspace with separate UI applications:

- `apps/web`: Next.js App Router for the public site and desktop browser/PWA experience.
- `apps/mobile`: Expo React Native for iOS and Android.
- `packages/shared`: platform-neutral app constants and Hello World types.
- `packages/supabase`: a public anon-key Supabase client factory that returns `null` when configuration is absent.

The existing `src/Longevity.Web` Vite application remains intact. It is a separate existing frontend and is not replaced by this milestone.

## Run locally

Use Node.js LTS from the repository root:

```text
npm install
npm run web:dev
npm run web:build
npm run mobile:start
npm run mobile:android
npm run mobile:ios
npm run mobile:web
npm run mobile:export
```

The web app runs at `http://localhost:3000`. Expo prints the mobile development URL and device/simulator options.

`npm run mobile:export` bundles the Expo app for iOS, Android, and web without requiring local simulators. Use `npm run mobile:android` on a machine with the Android SDK/emulator, and `npm run mobile:ios` on macOS with Xcode, for native device/simulator execution.

## Environment variables

Copy `.env.example` to the appropriate app directory when Supabase access is needed. Only the public Supabase URL and anon key belong in client applications. Never add service-role keys, passwords, access tokens, or database connection strings.

The Hello World screen does not require Supabase credentials. The client factories are intentionally nullable so the apps can boot without a configured backend.

## Evidence Explorer local development

The Next.js app uses the existing public evidence API through a server-side client. It does not access the private `workflow` schema or call Supabase directly for public registry pages.

```text
dotnet run --project src/Longevity.Api --launch-profile http
copy apps\web\.env.example apps\web\.env.local
npm run web:dev
```

The development API profile uses the explicit `PublicEvidence:Provider = Demo` setting and listens on `http://localhost:5271`. The Next.js pages are:

- `/assets` — bounded asset registry.
- `/assets/{slug}` — asset detail and linked claims.
- `/claims/{claimId}` — claim scores, evidence items, and source provenance.

`EVIDENCE_API_BASE_URL` is required in production; there is no silent provider fallback. `EVIDENCE_API_PROVIDER=Demo` controls the user-facing illustrative-data label and must match the API provider being run.

## Platform scope

Desktop is currently the responsive Next.js web/PWA target. A native desktop wrapper such as Tauri can be evaluated after the browser experience and product workflows are proven. iOS simulator builds require macOS/Xcode; Android builds require the Android SDK/emulator or a connected device.

This milestone makes no Supabase schema, migration, or seed-data changes.
