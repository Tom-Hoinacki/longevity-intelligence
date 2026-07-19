# Longevity Intelligence

An AI-ready longevity intelligence platform.

Goal:
Build the TradingView of longevity.

Core idea:
Asset â†’ Claim â†’ Source â†’ Evidence â†’ Score â†’ Verdict

Everything should be structured, source-backed, and expandable by both humans and AI agents.

## Cross-platform Hello World

The frontend foundation lives in an npm workspace alongside the existing .NET and Supabase code:

- `apps/web` â€” Next.js web app and responsive desktop/PWA target.
- `apps/mobile` â€” Expo React Native app for iOS and Android.
- `packages/shared` â€” shared platform-neutral types and app constants.
- `packages/supabase` â€” safe public Supabase client factory.

From the repository root:

```text
npm install
npm run web:dev
npm run web:build
npm run mobile:start
npm run mobile:android
npm run mobile:ios
npm run mobile:web
```

See [docs/architecture/cross-platform-foundation.md](docs/architecture/cross-platform-foundation.md) for environment variables, platform requirements, and scope notes. The existing Vite application under `src/Longevity.Web` is preserved and is not replaced by this milestone.

The Next.js app now includes the first Evidence Explorer slice. For local Demo-provider development, run the API and web app in separate terminals:

```text
dotnet run --project src/Longevity.Api --launch-profile http
copy apps\web\.env.example apps\web\.env.local
npm run web:dev
```

Then open `http://localhost:3000/assets`. The Demo provider is explicitly illustrative interface data; it is not medical evidence or a recommendation.

## Cloud development Supabase workflow

This repository is the schema source of truth for the confirmed disposable Supabase development project. Docker and local Supabase virtualization are intentionally skipped; production must use a separate Supabase project.

Use the repository-local CLI:

1. Authenticate with `npx.cmd supabase login` in an interactive shell.
2. Link only the confirmed development project with `npx.cmd supabase link --project-ref <development-project-ref>`.
3. Inspect history with `npx.cmd supabase migration list`.
4. Review changes with `npx.cmd supabase db push --dry-run --include-seed`.
5. After explicit human approval, deploy with `npx.cmd supabase db push --include-seed`.
6. Run `scripts/validate-data/validate_cloud_database.sql` in the Supabase SQL Editor using read-only validation queries.

Never use `supabase start`, `supabase db reset`, or `supabase db reset --linked` in this workflow. Never put passwords, access tokens, API keys, service-role keys, or database connection strings in this repository. Dashboard schema edits must be represented by migrations before deployment.

Cascade deletion is intentional: deleting an asset removes its claims, and deleting a claim or source removes dependent claim-evidence links. This keeps the educational evidence graph internally consistent; it must never be used for personal health data.

- `docs/architecture/market-intelligence-foundation.md` documents the public market-intelligence boundary for providers, commercial offerings, historical price observations, availability observations, and read-only API routes.

