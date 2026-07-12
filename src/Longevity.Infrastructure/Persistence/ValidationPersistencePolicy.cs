namespace Longevity.Infrastructure.Persistence;

public static class ValidationPersistencePolicy
{
    public const string LoadLatestCandidateBatchSql = """
        WITH latest_version AS (
            SELECT candidate_version
            FROM workflow.claim_candidates
            WHERE workflow_run_id = $1
            ORDER BY candidate_version DESC
            LIMIT 1
        )
        SELECT id, workflow_run_id, source_record_id, candidate_version,
               candidate_ordinal, claim_text, structured_candidate
        FROM workflow.claim_candidates
        WHERE workflow_run_id = $1
          AND candidate_version = (SELECT candidate_version FROM latest_version)
        ORDER BY candidate_ordinal;
        """;

    public const string UpdateValidationResultSql = """
        UPDATE workflow.claim_candidates
        SET deterministic_validation_status = $5,
            deterministic_validation_result = $6
        WHERE id = $1
          AND workflow_run_id = $2
          AND candidate_version = $3
          AND candidate_ordinal = $4;
        """;
}
