-- Read-only validation for the confirmed cloud development project.
-- Run in the Supabase SQL Editor. This script does not insert, update, delete,
-- alter, or drop anything.

select table_name
from information_schema.tables
where table_schema = 'public'
  and table_name in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
order by table_name;

select
  'expected_table_count' as validation_check,
  count(*) = 4 as passed,
  count(*) as actual_count
from information_schema.tables
where table_schema = 'public'
  and table_name in ('longevity_assets', 'claims', 'sources', 'claim_evidence');

select c.table_name, c.constraint_name, c.constraint_type
from information_schema.table_constraints c
where c.table_schema = 'public'
  and c.table_name in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
  and c.constraint_type in ('PRIMARY KEY', 'FOREIGN KEY')
order by c.table_name, c.constraint_type, c.constraint_name;

select n.nspname as schema_name, c.relname as table_name, c.relrowsecurity as rls_enabled
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'public'
  and c.relname in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
order by c.relname;

select schemaname, tablename, policyname, roles, cmd
from pg_policies
where schemaname = 'public'
  and tablename in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
order by tablename, policyname;

select
  'four_public_select_policies' as validation_check,
  count(*) = 4 as passed,
  count(*) as actual_count
from pg_policies
where schemaname = 'public'
  and tablename in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
  and cmd = 'SELECT'
  and roles @> array['anon', 'authenticated']::name[];

select
  'no_public_write_policies' as validation_check,
  count(*) = 0 as passed,
  count(*) as actual_count
from pg_policies
where schemaname = 'public'
  and tablename in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
  and cmd in ('INSERT', 'UPDATE', 'DELETE', 'ALL')
  and roles && array['anon', 'authenticated', 'public']::name[];

select slug, count(*) as asset_count
from public.longevity_assets
where slug = 'glp-1s'
group by slug;

select
  'single_glp_1_seed' as validation_check,
  count(*) = 1 as passed,
  count(*) as actual_count
from public.longevity_assets
where slug = 'glp-1s';

select conrelid::regclass as table_name, conname, pg_get_constraintdef(oid) as constraint_definition
from pg_constraint
where connamespace = 'public'::regnamespace
  and conrelid in ('public.longevity_assets'::regclass, 'public.claims'::regclass, 'public.sources'::regclass, 'public.claim_evidence'::regclass)
order by table_name, conname;

select version, name
from supabase_migrations.schema_migrations
order by version;

-- Compact pass/fail summary. Every row should report passed = true.
with validation_checks as (
  select
    'four_expected_tables' as validation_check,
    count(*) = 4 as passed,
    count(*)::text as actual
  from information_schema.tables
  where table_schema = 'public'
    and table_name in ('longevity_assets', 'claims', 'sources', 'claim_evidence')

  union all

  select
    'four_primary_keys',
    count(*) = 4,
    count(*)::text
  from information_schema.table_constraints
  where table_schema = 'public'
    and table_name in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
    and constraint_type = 'PRIMARY KEY'

  union all

  select
    'three_cascading_foreign_keys',
    count(*) = 3,
    count(*)::text
  from pg_constraint
  where connamespace = 'public'::regnamespace
    and contype = 'f'
    and conrelid in ('public.claims'::regclass, 'public.claim_evidence'::regclass)
    and pg_get_constraintdef(oid) like '%ON DELETE CASCADE%'

  union all

  select
    'rls_enabled_on_all_tables',
    count(*) = 4 and coalesce(bool_and(c.relrowsecurity), false),
    count(*)::text
  from pg_class c
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public'
    and c.relname in ('longevity_assets', 'claims', 'sources', 'claim_evidence')

  union all

  select
    'four_public_read_policies',
    count(*) = 4 and coalesce(bool_and(roles @> array['anon', 'authenticated']::name[]), false),
    count(*)::text
  from pg_policies
  where schemaname = 'public'
    and tablename in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
    and cmd = 'SELECT'

  union all

  select
    'no_public_write_policies',
    count(*) = 0,
    count(*)::text
  from pg_policies
  where schemaname = 'public'
    and tablename in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
    and cmd in ('INSERT', 'UPDATE', 'DELETE', 'ALL')
    and roles && array['anon', 'authenticated', 'public']::name[]

  union all

  select
    'one_glp_1_seed_asset',
    count(*) = 1,
    count(*)::text
  from public.longevity_assets
  where slug = 'glp-1s'

  union all

  select
    'five_score_range_checks',
    count(*) = 5,
    count(*)::text
  from pg_constraint
  where connamespace = 'public'::regnamespace
    and contype = 'c'
    and (
      pg_get_constraintdef(oid) like '%evidence_score%'
      or pg_get_constraintdef(oid) like '%hype_score%'
      or pg_get_constraintdef(oid) like '%risk_score%'
      or pg_get_constraintdef(oid) like '%source_quality_score%'
      or pg_get_constraintdef(oid) like '%relevance_score%'
    )

  union all

  select
    'migration_recorded',
    exists (
      select 1
      from supabase_migrations.schema_migrations
      where version = '20260711000000'
    ),
    coalesce((
      select version
      from supabase_migrations.schema_migrations
      where version = '20260711000000'
      limit 1
    ), 'missing')
)
select validation_check, passed, actual
from validation_checks
order by validation_check;
