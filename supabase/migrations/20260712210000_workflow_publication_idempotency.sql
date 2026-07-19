alter table public.claims
    add column evidence_scoring_policy_id text,
    add constraint claims_evidence_scoring_policy_check check (
        evidence_scoring_policy_id is null
        or (
            evidence_scoring_policy_id = btrim(evidence_scoring_policy_id)
            and length(evidence_scoring_policy_id) > 0
        )
    );

comment on column public.claims.evidence_scoring_policy_id is
  'Identity of the deterministic scoring policy that produced evidence_score. Null is retained for legacy rows.';

create table workflow.publications (
    id uuid primary key default gen_random_uuid(),
    idempotency_key text not null,
    content_fingerprint text not null,
    workflow_run_id uuid not null references workflow.runs(id) on delete restrict,
    workflow_run_version integer not null check (workflow_run_version >= 1),
    public_source_id uuid not null references public.sources(id) on delete restrict,
    public_claim_ids uuid[] not null,
    created_at timestamptz not null default now(),
    constraint workflow_publications_idempotency_key_key unique (idempotency_key),
    constraint workflow_publications_idempotency_key_check check (length(btrim(idempotency_key)) > 0 and idempotency_key = btrim(idempotency_key)),
    constraint workflow_publications_fingerprint_check check (content_fingerprint ~ '^[a-f0-9]{64}$'),
    constraint workflow_publications_claim_ids_check check (cardinality(public_claim_ids) > 0),
    constraint workflow_publications_run_version_key unique (workflow_run_id, workflow_run_version)
);

create index workflow_publications_source_idx on workflow.publications (public_source_id);
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
