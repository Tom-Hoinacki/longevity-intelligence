---
name: supabase-cloud-migration-safety
description: Use for reviewing, dry-running, validating, or deploying Supabase migrations and linked-project history; do not activate for unrelated application work or production changes without explicit authorization.
license: Proprietary project guidance
metadata:
  author: longevity-intelligence
  organization: longevity-intelligence
  compatibility: Windows PowerShell; use repository-local npx.cmd Supabase CLI against the linked development project
---

# Supabase Cloud Migration Safety

Use this skill for Supabase migrations, project linking, migration dry runs, deployment, RLS or privilege validation, and local/remote migration-history comparisons.

## Source of truth and project boundary

- Git migration files are the schema source of truth.
- Confirm the linked project name and project reference before any cloud change.
- Treat the documented linked project as development-only. Never select or modify production automatically.
- Preserve existing migrations. Never edit an already-applied migration.

## Required workflow

1. Inspect Git status, branch, complete diff, linked project metadata, and local/remote migration history.
2. Use Windows PowerShell and the repository-local CLI (`npx.cmd supabase ...`). Do not install global tools.
3. Run a dry run before a real push.
4. Stop for explicit human approval before a real push unless the current task contains unmistakable deployment approval.
5. After deployment, validate migration history, constraints, foreign keys, RLS, policies, privileges, default privileges, indexes, and preservation of existing schema protections.
6. Require fresh command evidence before claiming success.
7. Inspect the complete Git diff and scan all changed files for secrets before committing.

## Hard safety rules

- Never run `supabase db reset --linked`.
- Never use migration repair without explicit human authorization.
- Never automatically roll back, rewrite history, force push, or expose service-role credentials.
- Never modify production.
- If deployment succeeds but validation fails, perform read-only diagnosis and stop. Do not edit the applied migration or create an automatic corrective migration.
- Do not run destructive SQL, rollback SQL, or unrelated seed changes.
- Use no more than two thoughtful repair attempts for a repeated pre-deployment failure, then stop with evidence.

## Failure and Git rules

- If the linked project does not match the confirmed development project, stop before cloud changes.
- If there are unrelated working-tree changes, unexpected remote commits, a merge conflict, or a push conflict, stop and report them.
- If a pre-deployment command fails, make at most two careful, read-only or clearly bounded diagnostic attempts; never repair migration history automatically.
- Stage and commit only reviewed intended files. Do not claim a clean result without fresh `git status`, migration history, and post-deployment dry-run evidence.

This skill is instruction-only. Use the repository's existing validation scripts for database checks; do not add or execute downloaded skill scripts without reviewing them first.
