namespace Longevity.Infrastructure.Persistence;

public static class WorkflowIntakePersistencePolicy
{
    public const string InsertRunSql = """
        INSERT INTO workflow.runs (workflow_type, state, idempotency_key)
        VALUES ($1, 'source_normalized', $2)
        ON CONFLICT (workflow_type, idempotency_key) DO NOTHING
        RETURNING id, state, version;
        """;

    public const string LoadRunSql = """
        SELECT id, state, version
        FROM workflow.runs
        WHERE workflow_type = $1 AND idempotency_key = $2
        FOR SHARE;
        """;

    public const string LoadSourceSql = """
        SELECT source_identity_key, content_hash
        FROM workflow.source_records
        WHERE workflow_run_id = $1;
        """;

    public const string InsertSourceSql = """
        INSERT INTO workflow.source_records (
            workflow_run_id, source_type, source_identity_key, canonical_url,
            doi, pmid, clinicaltrials_id, title, normalized_text, content_hash,
            normalization_version)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11);
        """;
}
