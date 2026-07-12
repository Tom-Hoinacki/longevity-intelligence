namespace Longevity.Infrastructure.Persistence;

public static class HumanReviewPersistencePolicy
{
    public const string LoadPendingSql = """
        WITH pending_run AS (
            SELECT id, version, state
            FROM workflow.runs
            WHERE id = $1
              AND state = 'awaiting_human_approval'
        ),
        latest_version AS (
            SELECT max(candidate_version) AS candidate_version
            FROM workflow.claim_candidates
            WHERE workflow_run_id = $1
        )
        SELECT run.id, run.version, run.state,
               candidate.id, candidate.source_record_id, candidate.candidate_version,
               candidate.candidate_ordinal, candidate.claim_text,
               candidate.structured_candidate::text,
               candidate.deterministic_validation_status,
               candidate.deterministic_validation_result::text
        FROM pending_run AS run
        LEFT JOIN latest_version AS latest ON true
        LEFT JOIN workflow.claim_candidates AS candidate
          ON candidate.workflow_run_id = run.id
         AND candidate.candidate_version = latest.candidate_version
        ORDER BY candidate.candidate_ordinal;
        """;

    public const string LoadDecisionSql = """
        SELECT approval.workflow_run_id,
               approval.candidate_version,
               approval.expected_workflow_version,
               approval.decision_identity,
               approval.decision,
               approval.reviewer_subject,
               approval.rationale,
               approval.reviewer_note,
               approval.created_at,
               approval.target_state,
               count(*)::integer AS approval_count,
               (
                   SELECT count(*)::integer
                   FROM workflow.claim_candidates AS candidate
                   WHERE candidate.workflow_run_id = approval.workflow_run_id
                     AND candidate.candidate_version = approval.candidate_version
               ) AS candidate_count
        FROM workflow.approvals AS approval
        WHERE approval.decision_identity = $1
        GROUP BY approval.workflow_run_id,
                 approval.candidate_version,
                 approval.expected_workflow_version,
                 approval.decision_identity,
                 approval.decision,
                 approval.reviewer_subject,
                 approval.rationale,
                 approval.reviewer_note,
                 approval.created_at,
                 approval.target_state;
        """;

    public const string WorkflowRunExistsSql = """
        SELECT EXISTS (
            SELECT 1
            FROM workflow.runs
            WHERE id = $1
        );
        """;

    public const string LockDecisionIdentitySql = """
        SELECT pg_advisory_xact_lock(hashtextextended($1, 0));
        """;

    public const string LockWorkflowRunSql = """
        SELECT state, version
        FROM workflow.runs
        WHERE id = $1
        FOR UPDATE;
        """;

    public const string LoadEligibleCandidateBatchSql = """
        WITH latest_version AS (
            SELECT max(candidate_version) AS candidate_version
            FROM workflow.claim_candidates
            WHERE workflow_run_id = $1
        )
        SELECT candidate.id, candidate.workflow_run_id, candidate.source_record_id,
               candidate.candidate_version, candidate.candidate_ordinal,
               candidate.claim_text, candidate.structured_candidate::text,
               candidate.deterministic_validation_status,
               candidate.deterministic_validation_result::text
        FROM workflow.claim_candidates AS candidate
        WHERE candidate.workflow_run_id = $1
          AND candidate.candidate_version = (SELECT candidate_version FROM latest_version)
        ORDER BY candidate.candidate_ordinal
        FOR SHARE;
        """;

    public const string InsertApprovalSql = """
        INSERT INTO workflow.approvals (
            workflow_run_id,
            candidate_id,
            candidate_version,
            decision,
            reviewer_subject,
            rationale,
            created_at,
            decision_identity,
            expected_workflow_version,
            target_state,
            reviewer_note
        )
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11);
        """;

    public const string TransitionWorkflowRunSql = """
        UPDATE workflow.runs
        SET state = $4,
            updated_at = $5,
            completed_at = CASE WHEN $4 = 'rejected' THEN $5 ELSE completed_at END,
            version = version + 1
        WHERE id = $1
          AND state = 'awaiting_human_approval'
          AND version = $2
        RETURNING id, state, version;
        """;
}
