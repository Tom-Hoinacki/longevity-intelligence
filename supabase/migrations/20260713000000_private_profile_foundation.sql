-- Private profile data is intentionally isolated from public evidence and workflow schemas.
create schema if not exists private_profile;

revoke all on schema private_profile from public;
grant usage on schema private_profile to authenticated, service_role;

create table private_profile.profiles (
    profile_id uuid primary key default gen_random_uuid(),
    external_subject_id text not null unique,
    birth_date date,
    birth_year integer,
    sex_at_birth text,
    gender text,
    preferred_measurement_system text not null default 'metric',
    time_zone text not null default 'UTC',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint private_profiles_subject_check check (
        external_subject_id = btrim(external_subject_id)
        and length(external_subject_id) between 1 and 200
    ),
    constraint private_profiles_birth_fields_check check (
        not (birth_date is not null and birth_year is not null)
    ),
    constraint private_profiles_birth_year_check check (birth_year is null or birth_year between 1900 and 2100),
    constraint private_profiles_sex_at_birth_check check (
        sex_at_birth is null
        or sex_at_birth in ('female', 'male', 'intersex', 'unknown', 'not_disclosed')
    ),
    constraint private_profiles_gender_check check (
        gender is null
        or gender in ('woman', 'man', 'non_binary', 'gender_fluid', 'other', 'unknown', 'not_disclosed')
    ),
    constraint private_profiles_measurement_system_check check (
        preferred_measurement_system in ('metric', 'imperial')
    ),
    constraint private_profiles_time_zone_check check (
        time_zone = btrim(time_zone) and length(time_zone) between 1 and 64
    )
);

create table private_profile.consents (
    consent_id uuid primary key default gen_random_uuid(),
    profile_id uuid not null references private_profile.profiles(profile_id) on delete cascade,
    consent_type text not null,
    policy_version text not null,
    status text not null,
    granted_at timestamptz,
    withdrawn_at timestamptz,
    collection_source text not null,
    created_at timestamptz not null default now(),
    constraint private_consents_type_check check (
        consent_type in (
            'profile_data_storage',
            'personalized_analysis',
            'research_use',
            'deidentified_aggregate_data_use',
            'commercial_partner_matching'
        )
    ),
    constraint private_consents_status_check check (status in ('granted', 'declined')),
    constraint private_consents_timestamps_check check (
        (status = 'granted' and granted_at is not null and withdrawn_at is null)
        or (status = 'declined' and granted_at is null)
    ),
    constraint private_consents_text_check check (
        policy_version = btrim(policy_version)
        and length(policy_version) between 1 and 64
        and collection_source = btrim(collection_source)
        and length(collection_source) between 1 and 64
    )
);

create table private_profile.body_measurements (
    observation_id uuid primary key default gen_random_uuid(),
    profile_id uuid not null references private_profile.profiles(profile_id) on delete cascade,
    measurement_type text not null,
    numeric_value numeric(20, 8) not null,
    unit text not null,
    observed_at timestamptz not null,
    source_type text not null,
    source_label text,
    created_at timestamptz not null default now(),
    constraint private_body_measurements_type_check check (
        measurement_type = btrim(measurement_type)
        and length(measurement_type) between 1 and 100
    ),
    constraint private_body_measurements_value_check check (numeric_value > 0),
    constraint private_body_measurements_unit_check check (
        unit = btrim(unit) and length(unit) between 1 and 32
    ),
    constraint private_body_measurements_source_check check (
        source_type in ('self_reported', 'lab_report', 'clinician', 'imported', 'other')
    )
);

create table private_profile.lab_observations (
    observation_id uuid primary key default gen_random_uuid(),
    profile_id uuid not null references private_profile.profiles(profile_id) on delete cascade,
    test_name text not null,
    standardized_code_system text,
    standardized_test_code text,
    numeric_value numeric(24, 10),
    text_value text,
    unit text,
    reference_range_minimum numeric(24, 10),
    reference_range_maximum numeric(24, 10),
    abnormal_flag text,
    specimen_or_panel_label text,
    observed_at timestamptz not null,
    source_type text not null,
    source_label text,
    created_at timestamptz not null default now(),
    constraint private_lab_test_name_check check (
        test_name = btrim(test_name) and length(test_name) between 1 and 200
    ),
    constraint private_lab_value_kind_check check ((numeric_value is not null) <> (text_value is not null)),
    constraint private_lab_code_check check (standardized_test_code is null or standardized_code_system is not null),
    constraint private_lab_reference_range_check check (
        reference_range_minimum is null
        or reference_range_maximum is null
        or reference_range_minimum <= reference_range_maximum
    ),
    constraint private_lab_source_check check (
        source_type in ('self_reported', 'lab_report', 'clinician', 'imported', 'other')
    ),
    constraint private_lab_text_check check (
        (standardized_code_system is null or length(btrim(standardized_code_system)) between 1 and 64)
        and (standardized_test_code is null or length(btrim(standardized_test_code)) between 1 and 64)
        and (unit is null or length(btrim(unit)) between 1 and 32)
    )
);

create table private_profile.preferences (
    profile_id uuid primary key references private_profile.profiles(profile_id) on delete cascade,
    monthly_longevity_budget numeric(12, 2) not null,
    preferred_currency text not null,
    risk_tolerance text not null,
    evidence_confidence_preference text not null,
    cost_versus_confidence_preference text not null,
    insurance_use_preference text,
    updated_at timestamptz not null default now(),
    constraint private_preferences_budget_check check (monthly_longevity_budget between 0 and 1000000),
    constraint private_preferences_currency_check check (preferred_currency ~ '^[A-Z]{3}$'),
    constraint private_preferences_risk_check check (risk_tolerance in ('low', 'medium', 'high')),
    constraint private_preferences_evidence_check check (evidence_confidence_preference in ('minimum', 'balanced', 'highest')),
    constraint private_preferences_cost_check check (cost_versus_confidence_preference in ('lower_cost', 'balanced', 'higher_confidence')),
    constraint private_preferences_insurance_check check (
        insurance_use_preference is null
        or insurance_use_preference in ('prefer_use', 'avoid_use', 'no_preference')
    )
);

create table private_profile.goals (
    goal_id uuid primary key default gen_random_uuid(),
    profile_id uuid not null references private_profile.profiles(profile_id) on delete cascade,
    goal_type text not null,
    user_label text,
    priority integer not null,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint private_goals_type_check check (
        goal_type in (
            'general_healthy_aging',
            'cardiovascular_risk_reduction',
            'metabolic_health',
            'cognitive_health',
            'mobility_and_physical_function',
            'sleep',
            'appearance_or_skin_aging',
            'user_defined'
        )
    ),
    constraint private_goals_label_check check (
        user_label is null or (user_label = btrim(user_label) and length(user_label) between 1 and 200)
    ),
    constraint private_goals_user_defined_label_check check (goal_type <> 'user_defined' or user_label is not null),
    constraint private_goals_priority_check check (priority between 1 and 5)
);

create index private_consents_profile_created_idx
    on private_profile.consents (profile_id, created_at desc, consent_id desc);
create index private_body_measurements_profile_observed_idx
    on private_profile.body_measurements (profile_id, observed_at desc, observation_id desc);
create index private_lab_observations_profile_observed_idx
    on private_profile.lab_observations (profile_id, observed_at desc, observation_id desc);
create index private_goals_profile_priority_idx
    on private_profile.goals (profile_id, active desc, priority asc, created_at asc, goal_id asc);

create or replace function private_profile.current_subject()
returns text
language sql
stable
set search_path = ''
as $$
    select coalesce(
        nullif(current_setting('app.current_user_subject', true), ''),
        nullif(current_setting('request.jwt.claim.sub', true), '')
    )
$$;

alter table private_profile.profiles enable row level security;
alter table private_profile.consents enable row level security;
alter table private_profile.body_measurements enable row level security;
alter table private_profile.lab_observations enable row level security;
alter table private_profile.preferences enable row level security;
alter table private_profile.goals enable row level security;

alter table private_profile.profiles force row level security;
alter table private_profile.consents force row level security;
alter table private_profile.body_measurements force row level security;
alter table private_profile.lab_observations force row level security;
alter table private_profile.preferences force row level security;
alter table private_profile.goals force row level security;

create policy private_profiles_owner_policy
    on private_profile.profiles for all to public
    using (external_subject_id = (select private_profile.current_subject()))
    with check (external_subject_id = (select private_profile.current_subject()));

create policy private_consents_owner_policy
    on private_profile.consents for all to public
    using (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = consents.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ))
    with check (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = consents.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ));

create policy private_body_measurements_owner_policy
    on private_profile.body_measurements for all to public
    using (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = body_measurements.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ))
    with check (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = body_measurements.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ));

create policy private_lab_observations_owner_policy
    on private_profile.lab_observations for all to public
    using (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = lab_observations.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ))
    with check (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = lab_observations.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ));

create policy private_preferences_owner_policy
    on private_profile.preferences for all to public
    using (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = preferences.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ))
    with check (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = preferences.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ));

create policy private_goals_owner_policy
    on private_profile.goals for all to public
    using (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = goals.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ))
    with check (exists (
        select 1 from private_profile.profiles p
        where p.profile_id = goals.profile_id
          and p.external_subject_id = (select private_profile.current_subject())
    ));

grant execute on function private_profile.current_subject() to authenticated, service_role;
grant select, insert, update on private_profile.profiles, private_profile.preferences, private_profile.goals to authenticated, service_role;
grant select, insert on private_profile.consents, private_profile.body_measurements, private_profile.lab_observations to authenticated, service_role;

comment on schema private_profile is
    'Private, profile-owned data plane. Never mix these records with public evidence or workflow data.';
comment on table private_profile.consents is
    'Append-only consent events. The latest event per consent_type is the effective state; history is retained for auditability.';
comment on table private_profile.body_measurements is
    'Append-only body observations. The original numeric value and unit are preserved.';
comment on table private_profile.lab_observations is
    'Append-only structured lab observations. Document upload, OCR, and integrations are intentionally out of scope.';
