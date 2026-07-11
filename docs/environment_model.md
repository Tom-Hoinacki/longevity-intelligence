# Environment Model

- Local Git repository: version-controlled migrations, seed data, documentation, and validation SQL.
- Cloud development Supabase project: the disposable project explicitly confirmed for testing and dry-run deployment.
- Cloud production Supabase project: a separate project that must never be selected automatically.

Changes should move toward production only through reviewed migration files and an explicitly approved deployment workflow. The cloud development project is not production and must not contain production data or personal health data.
