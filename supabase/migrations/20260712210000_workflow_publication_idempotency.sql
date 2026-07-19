create table workflow.publications (
    id uuid primary key default gen_random_uuid(),
    idempotency_key text not null unique,
    content_fingerprint text not null,
    workflow_run_id uuid not null references workflow.runs(id) on delete restrict,
    workflow_run_version integer not null check (workflow_run_version >= 1),
    created_at timestamptz not null default now(),
    constraint workflow_publications_idempotency_key_check check (length(btrim(idempotency_key)) > 0),
    constraint workflow_publications_fingerprint_check check (content_fingerprint ~ '^[a-f0-9]{64}$')
);

create index workflow_publications_run_idx on workflow.publications (workflow_run_id);
revoke all on workflow.publications from public, anon, authenticated;
grant select, insert on workflow.publications to service_role;
alter table workflow.publications enable row level security;

grant select, insert on public.longevity_assets to service_role;
grant select, insert on public.claims to service_role;
grant select, insert on public.sources to service_role;
grant select, insert on public.claim_evidence to service_role;

create index if not exists claims_asset_idx on public.claims (asset_id);
create index if not exists claim_evidence_claim_idx on public.claim_evidence (claim_id);
create index if not exists claim_evidence_source_idx on public.claim_evidence (source_id);
