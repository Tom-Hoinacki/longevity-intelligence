-- Catalog-only validation for the private-profile migration.
-- Run as a migration administrator against a disposable database after applying migrations.
-- This script reads metadata only; it does not read or write private-profile rows.

do $$
declare
    private_table_count integer;
    protected_table_count integer;
    owner_policy_count integer;
    cascade_fk_count integer;
begin
    if not exists (
        select 1
        from pg_roles
        where rolname = 'private_profile_api'
          and not rolcanlogin
          and not rolsuper
          and not rolcreatedb
          and not rolcreaterole
          and not rolinherit
          and not rolbypassrls
    ) then
        raise exception 'private_profile_api role attributes are unsafe or missing';
    end if;

    if exists (
        select 1
        from pg_namespace namespace
        join pg_roles role on role.oid = namespace.nspowner
        where namespace.nspname = 'private_profile'
          and role.rolname = 'private_profile_api'
    ) or exists (
        select 1
        from pg_class relation
        join pg_namespace namespace on namespace.oid = relation.relnamespace
        join pg_roles role on role.oid = relation.relowner
        where namespace.nspname = 'private_profile'
          and role.rolname = 'private_profile_api'
    ) then
        raise exception 'private_profile_api must not own the schema or its relations';
    end if;

    select count(*) into private_table_count
    from pg_class relation
    join pg_namespace namespace on namespace.oid = relation.relnamespace
    where namespace.nspname = 'private_profile'
      and relation.relkind = 'r';

    select count(*) into protected_table_count
    from pg_class relation
    join pg_namespace namespace on namespace.oid = relation.relnamespace
    where namespace.nspname = 'private_profile'
      and relation.relkind = 'r'
      and relation.relrowsecurity
      and relation.relforcerowsecurity;

    if private_table_count <> 6 or protected_table_count <> private_table_count then
        raise exception 'expected all six private-profile tables to have enabled and forced RLS';
    end if;

    select count(*) into owner_policy_count
    from pg_policies
    where schemaname = 'private_profile'
      and roles = array['private_profile_api']::name[];

    if owner_policy_count <> 6 or exists (
        select 1
        from pg_policies
        where schemaname = 'private_profile'
          and roles <> array['private_profile_api']::name[]
    ) then
        raise exception 'private-profile policies are missing or target an unexpected role';
    end if;

    if exists (
        select 1
        from information_schema.table_privileges
        where table_schema = 'private_profile'
          and grantee in ('PUBLIC', 'anon', 'authenticated', 'service_role')
    ) then
        raise exception 'a public or Supabase browser/service role has private table privileges';
    end if;

    if has_schema_privilege('anon', 'private_profile', 'usage')
       or has_schema_privilege('authenticated', 'private_profile', 'usage')
       or has_schema_privilege('service_role', 'private_profile', 'usage') then
        raise exception 'a Supabase role has private schema usage';
    end if;

    if not has_schema_privilege('private_profile_api', 'private_profile', 'usage')
       or not has_table_privilege('private_profile_api', 'private_profile.profiles', 'select,insert,update')
       or has_table_privilege('private_profile_api', 'private_profile.profiles', 'delete,truncate')
       or not has_table_privilege('private_profile_api', 'private_profile.consents', 'select,insert')
       or has_table_privilege('private_profile_api', 'private_profile.consents', 'update,delete,truncate') then
        raise exception 'private_profile_api table privileges do not match the least-privilege contract';
    end if;

    if (select count(*)
        from information_schema.table_privileges
        where table_schema = 'private_profile'
          and grantee = 'private_profile_api') <> 15 then
        raise exception 'private_profile_api has an unexpected table privilege count';
    end if;

    if exists (
        select 1
        from information_schema.routine_privileges
        where specific_schema = 'private_profile'
          and routine_name = 'current_subject'
          and grantee in ('PUBLIC', 'anon', 'authenticated', 'service_role')
    ) or not has_function_privilege('private_profile_api', 'private_profile.current_subject()', 'execute') then
        raise exception 'current_subject function privileges do not match the backend-only contract';
    end if;

    if exists (
        select 1
        from pg_proc function
        join pg_namespace namespace on namespace.oid = function.pronamespace
        where namespace.nspname = 'private_profile'
          and function.proname = 'current_subject'
          and pg_get_functiondef(function.oid) ilike '%request.jwt.claim.sub%'
    ) then
        raise exception 'current_subject still accepts a direct JWT setting';
    end if;

    if not exists (
        select 1
        from pg_indexes
        where schemaname = 'private_profile'
          and indexname = 'private_consents_system_default_unique_idx'
          and indexdef ilike '%unique%'
          and indexdef ilike '%collection_source%system_default%'
    ) then
        raise exception 'default-consent idempotency index is missing';
    end if;

    select count(*) into cascade_fk_count
    from pg_constraint constraint_record
    join pg_class relation on relation.oid = constraint_record.conrelid
    join pg_namespace namespace on namespace.oid = relation.relnamespace
    where namespace.nspname = 'private_profile'
      and constraint_record.contype = 'f'
      and constraint_record.confdeltype = 'c';

    if cascade_fk_count <> 5 then
        raise exception 'expected five cascading private-profile foreign keys';
    end if;
end
$$;

select
    'private_profile_catalog_validation' as validation,
    'passed' as status;
