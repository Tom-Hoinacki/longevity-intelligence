create schema if not exists market_intelligence;

create table market_intelligence.providers (
    id uuid primary key default gen_random_uuid(),
    slug varchar(120) not null unique check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    display_name varchar(200) not null check (length(btrim(display_name)) between 1 and 200),
    provider_type varchar(40) not null check (provider_type in ('manufacturer','retailer','clinic','laboratory','pharmacy','subscription_service','marketplace','other')),
    canonical_website_url varchar(2048) check (canonical_website_url is null or canonical_website_url ~ '^https?://'),
    primary_region varchar(80),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table market_intelligence.offerings (
    id uuid primary key default gen_random_uuid(),
    asset_id uuid not null references public.longevity_assets(id) on delete restrict,
    provider_id uuid not null references market_intelligence.providers(id) on delete restrict,
    slug varchar(160) not null check (slug ~ '^[a-z0-9]+(?:-[a-z0-9]+)*$'),
    display_name varchar(240) not null check (length(btrim(display_name)) between 1 and 240),
    offering_type varchar(40) not null check (offering_type in ('physical_product','subscription','clinical_service','laboratory_test','membership','device','software_service','other')),
    provider_product_code varchar(120),
    package_quantity numeric(12,3) check (package_quantity is null or package_quantity > 0),
    package_unit varchar(80),
    billing_cadence varchar(80),
    canonical_url varchar(2048) check (canonical_url is null or canonical_url ~ '^https?://'),
    is_active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (provider_id, slug)
);

create table market_intelligence.price_observations (
    id uuid primary key default gen_random_uuid(),
    offering_id uuid not null references market_intelligence.offerings(id) on delete cascade,
    amount numeric(14,4) not null check (amount > 0),
    currency_code char(3) not null check (currency_code ~ '^[A-Z]{3}$'),
    pricing_basis varchar(40) not null check (pricing_basis in ('one_time_purchase','recurring_subscription','per_treatment','per_test','per_visit','per_unit','other')),
    billing_interval varchar(80),
    quantity numeric(12,3) check (quantity is null or quantity > 0),
    quantity_unit varchar(80),
    geographic_market varchar(80),
    observed_at timestamptz not null check (observed_at <= now() + interval '1 day'),
    source_url varchar(2048) not null check (source_url ~ '^https?://'),
    source_label varchar(200),
    created_at timestamptz not null default now()
);

create table market_intelligence.availability_observations (
    id uuid primary key default gen_random_uuid(),
    offering_id uuid not null references market_intelligence.offerings(id) on delete cascade,
    availability_status varchar(40) not null check (availability_status in ('available','out_of_stock','waitlist','unavailable','discontinued','unknown')),
    geographic_market varchar(80),
    observed_at timestamptz not null check (observed_at <= now() + interval '1 day'),
    source_url varchar(2048) not null check (source_url ~ '^https?://'),
    source_label varchar(200),
    created_at timestamptz not null default now()
);

create index providers_type_active_idx on market_intelligence.providers(provider_type, is_active);
create index offerings_asset_idx on market_intelligence.offerings(asset_id, is_active, slug);
create index offerings_provider_idx on market_intelligence.offerings(provider_id, is_active, slug);
create index price_observations_history_idx on market_intelligence.price_observations(offering_id, observed_at desc, id desc);
create index price_observations_observed_at_idx on market_intelligence.price_observations(observed_at desc, id desc);
create index availability_observations_history_idx on market_intelligence.availability_observations(offering_id, observed_at desc, id desc);
create index availability_observations_observed_at_idx on market_intelligence.availability_observations(observed_at desc, id desc);

alter table market_intelligence.providers enable row level security;
alter table market_intelligence.offerings enable row level security;
alter table market_intelligence.price_observations enable row level security;
alter table market_intelligence.availability_observations enable row level security;

grant usage on schema market_intelligence to anon, authenticated;
grant select on all tables in schema market_intelligence to anon, authenticated;

create policy "Public can read market providers" on market_intelligence.providers for select to anon, authenticated using (true);
create policy "Public can read market offerings" on market_intelligence.offerings for select to anon, authenticated using (true);
create policy "Public can read market price observations" on market_intelligence.price_observations for select to anon, authenticated using (true);
create policy "Public can read market availability observations" on market_intelligence.availability_observations for select to anon, authenticated using (true);
