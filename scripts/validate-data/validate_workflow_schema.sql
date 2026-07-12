-- Read-only catalog validation for the private workflow schema.
-- Every row in the final result must report passed = true before deployment.

with
workflow_tables(table_name) as (
    values ('runs'), ('source_records'), ('claim_candidates'), ('approvals')
),
workflow_table_oids as (
    select table_name, to_regclass('workflow.' || table_name) as table_oid
    from workflow_tables
),
workflow_constraint_defs as (
    select conname, contype, conrelid, pg_get_constraintdef(oid) as definition
    from pg_constraint
    where connamespace = to_regnamespace('workflow')
),
expected_states(state) as (
    values
        ('received'),
        ('source_normalized'),
        ('extracting'),
        ('candidate_extracted'),
        ('validating'),
        ('awaiting_human_approval'),
        ('approved'),
        ('publishing'),
        ('published'),
        ('no_candidate_extracted'),
        ('validation_failed'),
        ('rejected'),
        ('publication_failed')
),
expected_public_columns(table_name, column_name) as (
    values
        ('longevity_assets', 'id'), ('longevity_assets', 'slug'), ('longevity_assets', 'name'),
        ('claims', 'id'), ('claims', 'asset_id'), ('claims', 'claim_text'),
        ('sources', 'id'), ('sources', 'source_type'), ('sources', 'title'),
        ('claim_evidence', 'id'), ('claim_evidence', 'claim_id'), ('claim_evidence', 'source_id')
),
validation_checks as (
    select
        'workflow_schema_exists' as validation_check,
        to_regnamespace('workflow') is not null as passed,
        coalesce(to_regnamespace('workflow')::text, 'missing') as actual

    union all

    select
        'four_expected_workflow_tables',
        count(*) = 4 and count(*) filter (where table_oid is not null) = 4,
        count(*) filter (where table_oid is not null)::text
    from workflow_table_oids

    union all

    select
        'exact_primary_keys',
        count(*) = 4
        and not exists (
            select 1
            from workflow_table_oids t
            left join workflow_constraint_defs c
              on c.conrelid = t.table_oid
             and c.contype = 'p'
            where c.definition is distinct from 'PRIMARY KEY (id)'
        ),
        count(*)::text
    from workflow_constraint_defs
    where contype = 'p'

    union all

    select
        'exact_restrict_foreign_keys',
        count(*) = 5
        and bool_and(definition like '%ON DELETE RESTRICT%')
        and exists (
            select 1 from workflow_constraint_defs
            where conname = 'workflow_claim_candidates_source_run_fk'
              and definition like 'FOREIGN KEY (source_record_id, workflow_run_id) REFERENCES workflow.source_records(id, workflow_run_id)%'
        )
        and exists (
            select 1 from workflow_constraint_defs
            where conname = 'workflow_approvals_candidate_version_run_fk'
              and definition like 'FOREIGN KEY (candidate_id, candidate_version, workflow_run_id) REFERENCES workflow.claim_candidates(id, candidate_version, workflow_run_id)%'
        ),
        count(*)::text
    from workflow_constraint_defs
    where contype = 'f'

    union all

    select
        'rls_enabled_on_every_workflow_table',
        count(*) = 4 and coalesce(bool_and(c.relrowsecurity), false),
        count(*)::text
    from pg_class c
    where c.oid in (select table_oid from workflow_table_oids)

    union all

    select
        'zero_workflow_rls_policies',
        count(*) = 0,
        count(*)::text
    from pg_policies
    where schemaname = 'workflow'

    union all

    select
        'anon_authenticated_no_schema_usage',
        case when to_regnamespace('workflow') is null then false else
            not has_schema_privilege('anon', 'workflow', 'USAGE')
            and not has_schema_privilege('authenticated', 'workflow', 'USAGE')
        end,
        case when to_regnamespace('workflow') is null then 'schema missing' else 'checked' end

    union all

    select
        'public_no_schema_privileges',
        not exists (
            select 1
            from pg_namespace n
            cross join lateral aclexplode(coalesce(n.nspacl, acldefault('n', n.nspowner))) acl
            where n.oid = to_regnamespace('workflow')
              and acl.grantee = 0
        ),
        'checked'

    union all

    select
        'anon_authenticated_no_table_privileges',
        not exists (
            select 1
            from workflow_table_oids
            where table_oid is null
               or coalesce(has_table_privilege('anon', table_oid, 'SELECT'), false)
               or coalesce(has_table_privilege('anon', table_oid, 'INSERT'), false)
               or coalesce(has_table_privilege('anon', table_oid, 'UPDATE'), false)
               or coalesce(has_table_privilege('anon', table_oid, 'DELETE'), false)
               or coalesce(has_table_privilege('anon', table_oid, 'TRUNCATE'), false)
               or coalesce(has_table_privilege('authenticated', table_oid, 'SELECT'), false)
               or coalesce(has_table_privilege('authenticated', table_oid, 'INSERT'), false)
               or coalesce(has_table_privilege('authenticated', table_oid, 'UPDATE'), false)
               or coalesce(has_table_privilege('authenticated', table_oid, 'DELETE'), false)
               or coalesce(has_table_privilege('authenticated', table_oid, 'TRUNCATE'), false)
        ),
        'checked'

    union all

    select
        'public_no_table_privileges',
        not exists (
            select 1
            from pg_class c
            cross join lateral aclexplode(coalesce(c.relacl, acldefault('r', c.relowner))) acl
            where c.oid in (select table_oid from workflow_table_oids)
              and acl.grantee = 0
        ),
        'checked'

    union all

    select
        'service_role_bypasses_rls',
        exists (select 1 from pg_roles where rolname = 'service_role' and rolbypassrls),
        coalesce((select rolbypassrls::text from pg_roles where rolname = 'service_role'), 'missing')

    union all

    select
        'service_role_exact_runs_privileges',
        coalesce(has_table_privilege('service_role', to_regclass('workflow.runs'), 'SELECT'), false)
        and coalesce(has_table_privilege('service_role', to_regclass('workflow.runs'), 'INSERT'), false)
        and coalesce(has_table_privilege('service_role', to_regclass('workflow.runs'), 'UPDATE'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.runs'), 'DELETE'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.runs'), 'TRUNCATE'), false),
        'select, insert, update only'

    union all

    select
        'service_role_immutable_source_privileges',
        coalesce(has_table_privilege('service_role', to_regclass('workflow.source_records'), 'SELECT'), false)
        and coalesce(has_table_privilege('service_role', to_regclass('workflow.source_records'), 'INSERT'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.source_records'), 'UPDATE'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.source_records'), 'DELETE'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.source_records'), 'TRUNCATE'), false),
        'select, insert only'

    union all

    select
        'service_role_column_limited_candidate_updates',
        coalesce(has_table_privilege('service_role', to_regclass('workflow.claim_candidates'), 'SELECT'), false)
        and coalesce(has_table_privilege('service_role', to_regclass('workflow.claim_candidates'), 'INSERT'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.claim_candidates'), 'UPDATE'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.claim_candidates'), 'DELETE'), false)
        and coalesce(has_column_privilege('service_role', to_regclass('workflow.claim_candidates'), 'deterministic_validation_status', 'UPDATE'), false)
        and coalesce(has_column_privilege('service_role', to_regclass('workflow.claim_candidates'), 'deterministic_validation_result', 'UPDATE'), false)
        and not coalesce(has_column_privilege('service_role', to_regclass('workflow.claim_candidates'), 'claim_text', 'UPDATE'), false)
        and not coalesce(has_column_privilege('service_role', to_regclass('workflow.claim_candidates'), 'structured_candidate', 'UPDATE'), false),
        'select, insert, and two validation columns only'

    union all

    select
        'service_role_append_only_approval_privileges',
        coalesce(has_table_privilege('service_role', to_regclass('workflow.approvals'), 'SELECT'), false)
        and coalesce(has_table_privilege('service_role', to_regclass('workflow.approvals'), 'INSERT'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.approvals'), 'UPDATE'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.approvals'), 'DELETE'), false)
        and not coalesce(has_table_privilege('service_role', to_regclass('workflow.approvals'), 'TRUNCATE'), false),
        'select, insert only'

    union all

    select
        'human_review_decision_columns',
        count(*) = 4
        and bool_and(
            case column_name
                when 'decision_identity' then data_type = 'text' and is_nullable = 'NO'
                when 'expected_workflow_version' then data_type = 'integer' and is_nullable = 'YES'
                when 'target_state' then data_type = 'text' and is_nullable = 'YES'
                when 'reviewer_note' then data_type = 'text' and is_nullable = 'YES'
                else false
            end
        ),
        string_agg(column_name || ':' || data_type || ':nullable=' || is_nullable, ', ' order by column_name)
    from information_schema.columns
    where table_schema = 'workflow'
      and table_name = 'approvals'
      and column_name in ('decision_identity', 'expected_workflow_version', 'target_state', 'reviewer_note')

    union all

    select
        'human_review_decision_constraints',
        exists (
            select 1 from workflow_constraint_defs
            where conname = 'workflow_approvals_decision_identity_check'
              and definition like '%decision_identity%'
              and definition like '%btrim%'
        )
        and exists (
            select 1 from workflow_constraint_defs
            where conname = 'workflow_approvals_review_transition_check'
              and definition like '%expected_workflow_version%'
              and definition like '%target_state%'
              and definition like '%approved%'
              and definition like '%rejected%'
        )
        and exists (
            select 1 from workflow_constraint_defs
            where conname = 'workflow_approvals_rejection_reason_check'
              and definition like '%rationale%'
              and definition like '%rejected%'
        )
        and exists (
            select 1 from workflow_constraint_defs
            where conname = 'workflow_approvals_reviewer_note_check'
              and definition like '%reviewer_note%'
        ),
        'identity, transition, rejection reason, and reviewer note checks'

    union all

    select
        'human_review_decision_idempotency_index',
        exists (
            select 1
            from pg_index index_definition
            where index_definition.indexrelid = to_regclass('workflow.workflow_approvals_decision_candidate_key')
              and index_definition.indisunique
              and pg_get_indexdef(index_definition.indexrelid) like '%(decision_identity, candidate_id)%'
        ),
        coalesce(pg_get_indexdef(to_regclass('workflow.workflow_approvals_decision_candidate_key')), 'missing')

    union all

    select
        'human_review_columns_remain_append_only',
        not coalesce(has_column_privilege('service_role', to_regclass('workflow.approvals'), 'decision_identity', 'UPDATE'), false)
        and not coalesce(has_column_privilege('service_role', to_regclass('workflow.approvals'), 'expected_workflow_version', 'UPDATE'), false)
        and not coalesce(has_column_privilege('service_role', to_regclass('workflow.approvals'), 'target_state', 'UPDATE'), false)
        and not coalesce(has_column_privilege('service_role', to_regclass('workflow.approvals'), 'reviewer_note', 'UPDATE'), false),
        'service_role has no update privilege on human-review audit columns'

    union all

    select
        'safe_default_table_acl_and_identifiable_owner',
        exists (
            select 1 from pg_default_acl d
            where d.defaclnamespace = to_regnamespace('workflow')
              and d.defaclobjtype = 'r'
              and exists (
                  select 1 from aclexplode(coalesce(d.defaclacl, '{}'::aclitem[])) acl
                  where acl.grantee = (select oid from pg_roles where rolname = 'service_role')
                    and acl.privilege_type = 'SELECT'
              )
              and exists (
                  select 1 from aclexplode(coalesce(d.defaclacl, '{}'::aclitem[])) acl
                  where acl.grantee = (select oid from pg_roles where rolname = 'service_role')
                    and acl.privilege_type = 'INSERT'
              )
              and not exists (
                  select 1 from aclexplode(coalesce(d.defaclacl, '{}'::aclitem[])) acl
                  where acl.grantee = (select oid from pg_roles where rolname = 'service_role')
                    and acl.privilege_type in ('UPDATE', 'DELETE', 'TRUNCATE')
              )
        ),
        coalesce((
            select defaclrole::regrole::text
            from pg_default_acl
            where defaclnamespace = to_regnamespace('workflow') and defaclobjtype = 'r'
            limit 1
        ), 'missing')

    union all

    select
        'safe_default_sequence_acl',
        exists (
            select 1 from pg_default_acl d
            where d.defaclnamespace = to_regnamespace('workflow')
              and d.defaclobjtype = 'S'
              and exists (
                  select 1 from aclexplode(coalesce(d.defaclacl, '{}'::aclitem[])) acl
                  where acl.grantee = (select oid from pg_roles where rolname = 'service_role')
                    and acl.privilege_type = 'USAGE'
              )
              and not exists (
                  select 1 from aclexplode(coalesce(d.defaclacl, '{}'::aclitem[])) acl
                  where acl.grantee = (select oid from pg_roles where rolname = 'service_role')
                    and acl.privilege_type = 'UPDATE'
              )
        ),
        'usage and select only'

    union all

    select
        'complete_workflow_state_constraint',
        not exists (
            select 1 from expected_states s
            where not exists (
                select 1 from workflow_constraint_defs c
                where c.conname = 'workflow_runs_state_check'
                  and position(quote_literal(s.state) in c.definition) > 0
            )
        ),
        '13 states expected'

    union all

    select
        'approval_and_validation_status_constraints',
        exists (select 1 from workflow_constraint_defs where conname = 'workflow_approvals_decision_check' and definition like '%approved%' and definition like '%rejected%' and definition like '%revision_requested%')
        and exists (select 1 from workflow_constraint_defs where conname = 'workflow_claim_candidates_validation_status_check' and definition like '%pending%' and definition like '%passed%' and definition like '%failed%'),
        'approved/rejected/revision_requested; pending/passed/failed'

    union all

    select
        'score_and_nonnegative_constraints',
        exists (select 1 from workflow_constraint_defs where conname = 'workflow_claim_candidates_score_range_check' and definition like '%proposed_evidence_score%' and definition like '%proposed_hype_score%' and definition like '%proposed_risk_score%' and definition like '%0%' and definition like '%5%')
        and exists (select 1 from workflow_constraint_defs where conname = 'workflow_claim_candidates_usage_check' and definition like '%input_token_count%' and definition like '%output_token_count%' and definition like '%estimated_cost%' and definition like '%latency_ms%')
        and exists (select 1 from workflow_constraint_defs where conname = 'workflow_runs_retry_counts_check' and definition like '%retry_count%' and definition like '%max_retries%')
        and exists (select 1 from workflow_constraint_defs where conname = 'workflow_runs_version_check' and definition like '%version%')
        and exists (select 1 from workflow_constraint_defs where conname = 'workflow_claim_candidates_version_check' and definition like '%candidate_version%'),
        'scores 0..5; counts, costs, latency, retries, and versions nonnegative'

    union all

    select
        'claim_candidate_ordinal_column',
        exists (
            select 1
            from information_schema.columns
            where table_schema = 'workflow'
              and table_name = 'claim_candidates'
              and column_name = 'candidate_ordinal'
              and data_type = 'integer'
              and is_nullable = 'NO'
        ),
        coalesce((
            select data_type || ', nullable=' || is_nullable
            from information_schema.columns
            where table_schema = 'workflow'
              and table_name = 'claim_candidates'
              and column_name = 'candidate_ordinal'
        ), 'missing')

    union all

    select
        'claim_candidate_positive_ordinal_constraint',
        exists (
            select 1
            from workflow_constraint_defs
            where conname = 'workflow_claim_candidates_ordinal_check'
              and definition like '%candidate_ordinal%'
              and definition like '%1%'
        ),
        coalesce((
            select definition
            from workflow_constraint_defs
            where conname = 'workflow_claim_candidates_ordinal_check'
        ), 'missing')

    union all

    select
        'claim_candidate_version_ordinal_unique_constraint',
        exists (
            select 1
            from workflow_constraint_defs
            where conname = 'workflow_claim_candidates_source_version_ordinal_key'
              and contype = 'u'
              and definition = 'UNIQUE (source_record_id, candidate_version, candidate_ordinal)'
        )
        and not exists (
            select 1
            from workflow_constraint_defs
            where conname = 'workflow_claim_candidates_source_version_key'
              and contype = 'u'
        ),
        coalesce((
            select definition
            from workflow_constraint_defs
            where conname = 'workflow_claim_candidates_source_version_ordinal_key'
        ), 'missing; old two-column constraint must be absent')

    union all

    select
        'stable_source_identity_unique_index',
        exists (
            select 1
            from pg_index i
            where i.indexrelid = to_regclass('workflow.workflow_source_records_identity_idx')
              and i.indisunique
              and pg_get_indexdef(i.indexrelid) like '%(source_identity_key, content_hash, normalization_version)%'
        ),
        'source_identity_key, content_hash, normalization_version'

    union all

    select
        'one_source_record_per_workflow_run',
        exists (
            select 1
            from pg_index i
            where i.indexrelid = to_regclass('workflow.workflow_source_records_workflow_run_unique_idx')
              and i.indisunique
              and pg_get_indexdef(i.indexrelid) like '%(workflow_run_id)%'
        ),
        'unique workflow_run_id'

    union all

    select
        'runnable_work_index_definition',
        exists (
            select 1
            from pg_index i
            where i.indexrelid = to_regclass('workflow.workflow_runs_ready_idx')
              and pg_get_indexdef(i.indexrelid) like '%(state, available_at)%'
              and pg_get_indexdef(i.indexrelid) like '%retry_count < max_retries%'
              and pg_get_indexdef(i.indexrelid) like '%received%'
              and pg_get_indexdef(i.indexrelid) like '%source_normalized%'
              and pg_get_indexdef(i.indexrelid) like '%approved%'
              and pg_get_indexdef(i.indexrelid) not like '%awaiting_human_approval%'
        ),
        'state, available_at; automatic states only'

    union all

    select
        'public_evidence_tables_and_columns_remain',
        count(*) = 4
        and not exists (
            select 1
            from expected_public_columns e
            where not exists (
                select 1
                from information_schema.columns c
                where c.table_schema = 'public'
                  and c.table_name = e.table_name
                  and c.column_name = e.column_name
            )
        ),
        'four tables and required columns'
    from information_schema.tables
    where table_schema = 'public'
      and table_name in ('longevity_assets', 'claims', 'sources', 'claim_evidence')

    union all

    select
        'public_evidence_rls_and_read_only_policies_remain',
        (
            select count(*) = 4 and coalesce(bool_and(c.relrowsecurity), false)
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'public'
              and c.relname in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
        )
        and (
            select count(*) = 4
            from pg_policies
            where schemaname = 'public'
              and tablename in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
              and cmd = 'SELECT'
              and roles @> array['anon', 'authenticated']::name[]
        )
        and (
            select count(*) = 0
            from pg_policies
            where schemaname = 'public'
              and tablename in ('longevity_assets', 'claims', 'sources', 'claim_evidence')
              and cmd in ('INSERT', 'UPDATE', 'DELETE', 'ALL')
        ),
        'RLS enabled; four public SELECT policies; no public writes'
)
select validation_check, passed, actual
from validation_checks
order by validation_check;

-- Diagnostic only: identifies the role whose future-object defaults this migration changed.
select
    defaclrole::regrole as default_acl_owner,
    defaclobjtype as object_type,
    defaclnamespace::regnamespace as schema_name
from pg_default_acl
where defaclnamespace = to_regnamespace('workflow')
order by defaclobjtype, default_acl_owner;
