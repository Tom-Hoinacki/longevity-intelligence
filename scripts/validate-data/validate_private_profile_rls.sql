-- Transactional RLS smoke test for a disposable database after migrations.
-- Uses only fictional rows and rolls the entire test back.

begin;
set local role private_profile_api;

do $$
declare
    subject_a constant text := 'security-review-fictional-subject-a';
    subject_b constant text := 'security-review-fictional-subject-b';
    profile_a uuid;
    profile_b uuid;
begin
    perform set_config('app.current_user_subject', subject_a, true);
    insert into private_profile.profiles (external_subject_id)
    values (subject_a)
    returning profile_id into profile_a;

    insert into private_profile.body_measurements
        (profile_id, measurement_type, numeric_value, unit, observed_at, source_type)
    values
        (profile_a, 'weight', 70, 'kg', clock_timestamp(), 'self_reported');

    insert into private_profile.lab_observations
        (profile_id, test_name, numeric_value, unit, observed_at, source_type)
    values
        (profile_a, 'Fictional security marker', 1, 'fictional-unit', clock_timestamp(), 'self_reported');

    insert into private_profile.consents
        (profile_id, consent_type, policy_version, status, collection_source)
    values
        (profile_a, 'research_use', 'foundation-v1', 'declined', 'system_default');

    begin
        insert into private_profile.consents
            (profile_id, consent_type, policy_version, status, collection_source)
        values
            (profile_a, 'research_use', 'foundation-v1', 'declined', 'system_default');
        raise exception 'duplicate system-default consent unexpectedly succeeded';
    exception
        when unique_violation then null;
    end;

    perform set_config('app.current_user_subject', subject_b, true);

    if exists (select 1 from private_profile.profiles where profile_id = profile_a)
       or exists (select 1 from private_profile.body_measurements where profile_id = profile_a)
       or exists (select 1 from private_profile.lab_observations where profile_id = profile_a) then
        raise exception 'subject B can read subject A data';
    end if;

    update private_profile.profiles
    set time_zone = 'UTC'
    where profile_id = profile_a;
    if found then
        raise exception 'subject B can update subject A profile';
    end if;

    begin
        insert into private_profile.body_measurements
            (profile_id, measurement_type, numeric_value, unit, observed_at, source_type)
        values
            (profile_a, 'weight', 71, 'kg', clock_timestamp(), 'self_reported');
        raise exception 'subject B can insert a child row for subject A';
    exception
        when insufficient_privilege then null;
    end;

    insert into private_profile.profiles (external_subject_id)
    values (subject_b)
    returning profile_id into profile_b;

    if not exists (select 1 from private_profile.profiles where profile_id = profile_b) then
        raise exception 'subject B cannot read its own profile';
    end if;

    perform set_config('app.current_user_subject', subject_a, true);
    if exists (select 1 from private_profile.profiles where profile_id = profile_b) then
        raise exception 'subject A can read subject B profile';
    end if;
end
$$;

reset role;
rollback;

select
    'private_profile_transactional_rls_validation' as validation,
    'passed' as status;
