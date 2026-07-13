# Longevity Intelligence

An AI-ready longevity intelligence platform.

Goal:
Build the TradingView of longevity.

Core idea:
Asset → Claim → Source → Evidence → Score → Verdict

Everything should be structured, source-backed, and expandable by both humans and AI agents.

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
