alter table workflow.approvals
    add column decision_identity text,
    add column expected_workflow_version integer,
    add column target_state text,
    add column reviewer_note text;

update workflow.approvals
set decision_identity = id::text
where decision_identity is null;

alter table workflow.approvals
    alter column decision_identity set not null,
    add constraint workflow_approvals_decision_identity_check
        check (
            length(btrim(decision_identity)) > 0
            and decision_identity = btrim(decision_identity)
        ),
    add constraint workflow_approvals_review_transition_check
        check (
            (
                expected_workflow_version is null
                and target_state is null
            )
            or
            (
                expected_workflow_version >= 1
                and (
                    (decision = 'approved' and target_state = 'approved')
                    or (decision = 'rejected' and target_state = 'rejected')
                )
            )
        ),
    add constraint workflow_approvals_rejection_reason_check
        check (
            expected_workflow_version is null
            or decision <> 'rejected'
            or (rationale is not null and length(btrim(rationale)) > 0)
        ),
    add constraint workflow_approvals_reviewer_note_check
        check (
            reviewer_note is null
            or (
                reviewer_note = btrim(reviewer_note)
                and length(reviewer_note) > 0
            )
        ),
    add constraint workflow_approvals_decision_candidate_key
        unique (decision_identity, candidate_id);

comment on column workflow.approvals.decision_identity is
  'Client-supplied immutable decision identity. One identity groups the per-candidate rows for an atomic batch decision.';
comment on column workflow.approvals.expected_workflow_version is
  'Optimistic-concurrency version observed before the human-review transition. Null only for decisions predating this migration.';
comment on column workflow.approvals.target_state is
  'Server-derived workflow target for this decision. Null only for decisions predating this migration.';
comment on column workflow.approvals.reviewer_note is
  'Optional trusted reviewer note. This private audit value must never be returned in error responses or logs.';

revoke update (decision_identity, expected_workflow_version, target_state, reviewer_note)
    on workflow.approvals from service_role, anon, authenticated;
