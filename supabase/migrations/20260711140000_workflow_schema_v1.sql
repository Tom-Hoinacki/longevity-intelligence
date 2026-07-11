create schema workflow;

comment on schema workflow is
  'Private AI evidence-ingestion workflow data. Do not add this schema to public API configuration.';

revoke all on schema workflow from public, anon, authenticated;
grant usage on schema workflow to service_role;

create table workflow.runs (
    id uuid primary key default gen_random_uuid(),
    workflow_type text not null,
    state text not null default 'received',
    idempotency_key text not null,
    retry_count integer not null default 0,
    max_retries integer not null default 3,
    last_error_code text,
    last_error_summary text,
    available_at timestamptz not null default now(),
    started_at timestamptz,
    completed_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version integer not null default 1,
    constraint workflow_runs_state_check check (state in (
        'received',
        'source_normalized',
        'extracting',
        'candidate_extracted',
        'validating',
        'awaiting_human_approval',
        'approved',
        'publishing',
        'published',
        'no_candidate_extracted',
        'validation_failed',
        'rejected',
        'publication_failed'
    )),
    constraint workflow_runs_retry_counts_check check (
        retry_count >= 0
        and max_retries >= 0
        and retry_count <= max_retries
    ),
    constraint workflow_runs_version_check check (version >= 1),
    constraint workflow_runs_audit_fields_check check (
        length(btrim(workflow_type)) > 0
        and length(btrim(idempotency_key)) > 0
    ),
    constraint workflow_runs_type_idempotency_key unique (workflow_type, idempotency_key)
);

comment on table workflow.runs is
  'Durable, optimistic-concurrency workflow executions. State transitions are orchestrator-controlled.';

create index workflow_runs_ready_idx
on workflow.runs (state, available_at)
where state in (
    'received',
    'source_normalized',
    'extracting',
    'candidate_extracted',
    'validating',
    'approved',
    'publishing'
)
and retry_count < max_retries;

create table workflow.source_records (
    id uuid primary key default gen_random_uuid(),
    workflow_run_id uuid not null,
    source_type text not null,
    source_identity_key text not null,
    canonical_url text,
    doi text,
    pmid text,
    clinicaltrials_id text,
    title text not null,
    publication_name text,
    authors jsonb not null default '[]'::jsonb,
    published_date date,
    normalized_text text not null,
    content_hash text not null,
    normalization_version text not null,
    source_metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint workflow_source_records_run_fk
        foreign key (workflow_run_id)
        references workflow.runs(id)
        on delete restrict,
    constraint workflow_source_records_id_run_key unique (id, workflow_run_id),
    constraint workflow_source_records_text_check
        check (length(btrim(normalized_text)) > 0),
    constraint workflow_source_records_audit_fields_check
        check (
            length(btrim(source_type)) > 0
            and length(btrim(source_identity_key)) > 0
            and source_identity_key = btrim(source_identity_key)
            and length(btrim(title)) > 0
            and length(btrim(normalization_version)) > 0
        ),
    constraint workflow_source_records_hash_check
        check (content_hash ~ '^[a-f0-9]{64}$'),
    constraint workflow_source_records_identifier_format_check
        check (
            (canonical_url is null or (
                length(btrim(canonical_url)) > 0
                and canonical_url = btrim(canonical_url)
            ))
            and (doi is null or (
                length(btrim(doi)) > 0
                and doi = lower(btrim(doi))
                and doi !~* '(^https?://|^doi:|doi\\.org/)'
            ))
            and (pmid is null or (pmid = btrim(pmid) and pmid ~ '^[0-9]+$'))
            and (clinicaltrials_id is null or (
                clinicaltrials_id = btrim(clinicaltrials_id)
                and clinicaltrials_id ~ '^NCT[0-9]{8}$'
            ))
        ),
    constraint workflow_source_records_identity_key_check
        check (
            (doi is not null and source_identity_key = ('doi:' || doi))
            or (doi is null and pmid is not null and source_identity_key = ('pmid:' || pmid))
            or (
                doi is null
                and pmid is null
                and clinicaltrials_id is not null
                and source_identity_key = ('clinicaltrials:' || lower(clinicaltrials_id))
            )
            or (
                doi is null
                and pmid is null
                and clinicaltrials_id is null
                and canonical_url is not null
                and source_identity_key = ('url:' || canonical_url)
            )
        ),
    constraint workflow_source_records_authors_check
        check (jsonb_typeof(authors) in ('array', 'object')),
    constraint workflow_source_records_metadata_check
        check (jsonb_typeof(source_metadata) = 'object')
);

comment on table workflow.source_records is
  'Versioned normalized authoritative inputs. Normalized text is stored directly in Postgres for v1 reproducibility.';

create index workflow_source_records_run_idx
on workflow.source_records (workflow_run_id);

create unique index workflow_source_records_identity_idx
on workflow.source_records (
    source_identity_key,
    content_hash,
    normalization_version
);

create table workflow.claim_candidates (
    id uuid primary key default gen_random_uuid(),
    workflow_run_id uuid not null,
    source_record_id uuid not null,
    candidate_version integer not null,
    schema_version text not null,
    claim_text text not null,
    proposed_asset_slug text,
    proposed_asset_name text,
    population text,
    intervention text,
    comparator text,
    outcome text,
    evidence_level text,
    evidence_direction text,
    limitations text,
    supporting_excerpts jsonb not null default '[]'::jsonb,
    proposed_evidence_score numeric(3,1),
    proposed_hype_score numeric(3,1),
    proposed_risk_score numeric(3,1),
    structured_candidate jsonb not null,
    deterministic_validation_status text not null default 'pending',
    deterministic_validation_result jsonb not null default '{}'::jsonb,
    model_provider text not null,
    model_name text not null,
    prompt_version text not null,
    input_token_count integer,
    output_token_count integer,
    estimated_cost numeric(12,6),
    latency_ms integer,
    trace_identifier text,
    created_at timestamptz not null default now(),
    constraint workflow_claim_candidates_run_fk
        foreign key (workflow_run_id)
        references workflow.runs(id)
        on delete restrict,
    constraint workflow_claim_candidates_source_run_fk
        foreign key (source_record_id, workflow_run_id)
        references workflow.source_records(id, workflow_run_id)
        on delete restrict,
    constraint workflow_claim_candidates_source_version_key
        unique (source_record_id, candidate_version),
    constraint workflow_claim_candidates_id_version_run_key
        unique (id, candidate_version, workflow_run_id),
    constraint workflow_claim_candidates_version_check
        check (candidate_version >= 1),
    constraint workflow_claim_candidates_claim_text_check
        check (length(btrim(claim_text)) > 0),
    constraint workflow_claim_candidates_audit_fields_check
        check (
            length(btrim(schema_version)) > 0
            and length(btrim(model_provider)) > 0
            and length(btrim(model_name)) > 0
            and length(btrim(prompt_version)) > 0
        ),
    constraint workflow_claim_candidates_score_range_check
        check (
            (proposed_evidence_score is null or proposed_evidence_score between 0 and 5)
            and (proposed_hype_score is null or proposed_hype_score between 0 and 5)
            and (proposed_risk_score is null or proposed_risk_score between 0 and 5)
        ),
    constraint workflow_claim_candidates_validation_status_check
        check (deterministic_validation_status in ('pending', 'passed', 'failed')),
    constraint workflow_claim_candidates_artifacts_check
        check (
            jsonb_typeof(supporting_excerpts) = 'array'
            and jsonb_typeof(structured_candidate) = 'object'
            and jsonb_typeof(deterministic_validation_result) = 'object'
        ),
    constraint workflow_claim_candidates_usage_check
        check (
            (input_token_count is null or input_token_count >= 0)
            and (output_token_count is null or output_token_count >= 0)
            and (estimated_cost is null or estimated_cost >= 0)
            and (latency_ms is null or latency_ms >= 0)
        )
);

comment on table workflow.claim_candidates is
  'Private, versioned model output. Proposed scores and evidence fields are candidates, not scientific certainty.';

create index workflow_claim_candidates_run_idx
on workflow.claim_candidates (workflow_run_id, created_at);

create table workflow.approvals (
    id uuid primary key default gen_random_uuid(),
    workflow_run_id uuid not null,
    candidate_id uuid not null,
    candidate_version integer not null,
    decision text not null,
    reviewer_subject text not null,
    rationale text,
    created_at timestamptz not null default now(),
    constraint workflow_approvals_run_fk
        foreign key (workflow_run_id)
        references workflow.runs(id)
        on delete restrict,
    constraint workflow_approvals_candidate_version_run_fk
        foreign key (candidate_id, candidate_version, workflow_run_id)
        references workflow.claim_candidates(id, candidate_version, workflow_run_id)
        on delete restrict,
    constraint workflow_approvals_decision_check
        check (decision in ('approved', 'rejected', 'revision_requested')),
    constraint workflow_approvals_candidate_version_check
        check (
            candidate_version >= 1
            and length(btrim(reviewer_subject)) > 0
        )
);

comment on table workflow.approvals is
  'Append-only human review history. Granting update or delete access would violate the audit boundary.';

create index workflow_approvals_run_idx
on workflow.approvals (workflow_run_id, created_at);

create index workflow_approvals_candidate_idx
on workflow.approvals (candidate_id, candidate_version, created_at);

revoke all on all tables in schema workflow from public, anon, authenticated;
revoke all on all sequences in schema workflow from public, anon, authenticated;

grant select, insert, update on workflow.runs to service_role;
grant select, insert on workflow.source_records to service_role;
grant select, insert on workflow.claim_candidates to service_role;
grant update (deterministic_validation_status, deterministic_validation_result)
    on workflow.claim_candidates to service_role;
grant select, insert on workflow.approvals to service_role;

alter default privileges in schema workflow
    revoke all on tables from public, anon, authenticated;
alter default privileges in schema workflow
    revoke all on sequences from public, anon, authenticated;
alter default privileges in schema workflow
    grant select, insert on tables to service_role;
alter default privileges in schema workflow
    grant usage, select on sequences to service_role;

alter table workflow.runs enable row level security;
alter table workflow.source_records enable row level security;
alter table workflow.claim_candidates enable row level security;
alter table workflow.approvals enable row level security;

comment on column workflow.runs.version is
  'Incremented by the orchestrator on update to enforce optimistic concurrency.';
comment on column workflow.source_records.content_hash is
  'Lowercase SHA-256 hash of normalized source text.';
comment on column workflow.source_records.source_identity_key is
  'Deterministic normalized identity: DOI, PMID, ClinicalTrials.gov identifier, or canonical URL in that priority order.';
comment on column workflow.claim_candidates.structured_candidate is
  'Versioned provider-independent typed candidate artifact.';
comment on column workflow.claim_candidates.deterministic_validation_result is
  'Machine-readable checks; this does not replace human scientific review.';
