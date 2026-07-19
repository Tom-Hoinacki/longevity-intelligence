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
        SELECT candidate.id, candidate.workflow_run_id, candidate.source_record_id,
               candidate.candidate_version, candidate.candidate_ordinal,
               candidate.claim_text, candidate.structured_candidate,
               source.normalized_text
        FROM workflow.claim_candidates AS candidate
        JOIN workflow.source_records AS source
          ON source.id = candidate.source_record_id
         AND source.workflow_run_id = candidate.workflow_run_id
        WHERE candidate.workflow_run_id = $1
          AND candidate.candidate_version = (SELECT candidate_version FROM latest_version)
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
