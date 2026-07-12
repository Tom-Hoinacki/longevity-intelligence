namespace Longevity.Infrastructure.Persistence;

public static class ClaimExtractionPersistencePolicy
{
    public const string LoadNormalizedSourceSql = """
        SELECT id, workflow_run_id, source_identity_key, title, normalized_text
        FROM workflow.source_records
        WHERE workflow_run_id = $1;
        """;

    public const string InsertClaimCandidateSql = """
        INSERT INTO workflow.claim_candidates (
            workflow_run_id,
            source_record_id,
            candidate_version,
            candidate_ordinal,
            schema_version,
            claim_text,
            structured_candidate,
            model_provider,
            model_name,
            prompt_version,
            input_token_count,
            output_token_count,
            estimated_cost,
            latency_ms,
            trace_identifier
        )
        VALUES (
            $1, $2, $3, $4, $5, $6, $7, $8, $9, $10,
            $11, $12, $13, $14, $15
        );
        """;
}
