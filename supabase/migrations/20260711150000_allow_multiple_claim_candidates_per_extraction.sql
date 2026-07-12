-- Allow one extraction-output version to contain multiple claim candidates.
-- Existing rows are assigned ordinal 1; future extraction code must assign
-- distinct positive ordinals within each source record and candidate version.

alter table workflow.claim_candidates
    add column candidate_ordinal integer;

update workflow.claim_candidates
set candidate_ordinal = 1
where candidate_ordinal is null;

alter table workflow.claim_candidates
    alter column candidate_ordinal set not null;

alter table workflow.claim_candidates
    add constraint workflow_claim_candidates_ordinal_check
        check (candidate_ordinal >= 1),
    drop constraint workflow_claim_candidates_source_version_key,
    add constraint workflow_claim_candidates_source_version_ordinal_key
        unique (source_record_id, candidate_version, candidate_ordinal);

comment on column workflow.claim_candidates.candidate_version is
    'Identifies one extraction-output version for a source record.';

comment on column workflow.claim_candidates.candidate_ordinal is
    'Identifies an individual claim within an extraction-output version; ordinals begin at 1.';
