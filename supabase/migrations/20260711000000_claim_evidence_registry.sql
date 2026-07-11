create table public.longevity_assets (
    id uuid primary key default gen_random_uuid(),
    slug text not null unique,
    name text not null,
    asset_type text not null,
    short_summary text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table public.longevity_assets enable row level security;

create policy "Public can read longevity assets"
on public.longevity_assets
for select
to anon, authenticated
using (true);

create table public.claims (
    id uuid primary key default gen_random_uuid(),
    asset_id uuid not null references public.longevity_assets(id) on delete cascade,
    claim_text text not null,
    claim_type text,
    target_system text,
    evidence_score numeric(3,1) check (evidence_score between 0 and 5),
    hype_score numeric(3,1) check (hype_score between 0 and 5),
    risk_score numeric(3,1) check (risk_score between 0 and 5),
    plain_english_verdict text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table public.claims enable row level security;

create policy "Public can read claims"
on public.claims
for select
to anon, authenticated
using (true);

create table public.sources (
    id uuid primary key default gen_random_uuid(),
    source_type text not null,
    title text not null,
    url text,
    publication_name text,
    published_date date,
    doi text,
    pmid text,
    trial_id text,
    source_quality_score numeric(3,1) check (source_quality_score between 0 and 5),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table public.sources enable row level security;

create policy "Public can read sources"
on public.sources
for select
to anon, authenticated
using (true);

create table public.claim_evidence (
    id uuid primary key default gen_random_uuid(),
    claim_id uuid not null references public.claims(id) on delete cascade,
    source_id uuid not null references public.sources(id) on delete cascade,
    evidence_direction text not null,
    evidence_level text not null,
    population text,
    outcome_measured text,
    effect_summary text,
    limitations text,
    relevance_score numeric(3,1) check (relevance_score between 0 and 5),
    created_at timestamptz not null default now()
);

alter table public.claim_evidence enable row level security;

create policy "Public can read claim evidence"
on public.claim_evidence
for select
to anon, authenticated
using (true);
