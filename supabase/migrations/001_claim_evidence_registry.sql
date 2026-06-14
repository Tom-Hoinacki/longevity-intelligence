create extension if not exists "uuid-ossp";
create table longevity_assets (
    id uuid primary key default uuid_generate_v4(),
    slug text not null unique,
    name text not null,
    asset_type text not null,
    short_summary text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table longevity_assets enable row level security;

create policy "Public can read longevity assets"
on longevity_assets
for select
to anon, authenticated
using (true);

create table claims (
    id uuid primary key default uuid_generate_v4(),
    asset_id uuid not null references longevity_assets(id) on delete cascade,
    claim_text text not null,
    claim_type text,
    target_system text,
    evidence_score numeric(3,1),
    hype_score numeric(3,1),
    risk_score numeric(3,1),
    plain_english_verdict text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table claims enable row level security;

create policy "Public can read claims"
on claims
for select
to anon, authenticated
using (true);

create table sources (
    id uuid primary key default uuid_generate_v4(),
    source_type text not null,
    title text not null,
    url text,
    publication_name text,
    published_date date,
    doi text,
    pmid text,
    trial_id text,
    source_quality_score numeric(3,1),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table sources enable row level security;

create policy "Public can read sources"
on sources
for select
to anon, authenticated
using (true);

create table claim_evidence (
    id uuid primary key default uuid_generate_v4(),
    claim_id uuid not null references claims(id) on delete cascade,
    source_id uuid not null references sources(id) on delete cascade,
    evidence_direction text not null,
    evidence_level text not null,
    population text,
    outcome_measured text,
    effect_summary text,
    limitations text,
    relevance_score numeric(3,1),
    created_at timestamptz not null default now()
);

alter table claim_evidence enable row level security;

create policy "Public can read claim evidence"
on claim_evidence
for select
to anon, authenticated
using (true);