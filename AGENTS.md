# Agent Instructions

Mission:
Build the structured intelligence layer for longevity.

Core Rule:
Every claim must be backed by evidence and sources.

Do Not:
- Give medical advice
- Make unsupported health claims
- Mix sponsorship with evidence scores
- Store user health data in the public evidence database
- Change schema without a migration

Workflow:
1. Create or update migrations
2. Add or update seed data
3. Validate data
4. Update documentation
5. Explain changes before committing

Core Data Model:
Asset → Claim → Source → Evidence

Cloud development rules:
- Migration files are the schema source of truth.
- Do not edit schema directly in the dashboard unless the change is also captured in a migration.
- Always run a dry run before deployment and require human approval before cloud changes.
- Never select or connect to a production project automatically.
- Never use `supabase db reset --linked`.
- Never expose or store secrets in the repository.
- Never store personal health data in the public educational schema.

Repository skills:
- Repository-scoped skills live in `.agents/skills` and should be used when a task matches their descriptions.
- Project-specific safety rules in this file override generic external advice.
- Never execute newly downloaded skill scripts without reviewing them first.
