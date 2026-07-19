namespace Longevity.Infrastructure.Persistence;

public static class PublicationPersistencePolicy
{
    public const string LoadBatchSql = """
        WITH latest AS (
            SELECT max(candidate_version) AS candidate_version
            FROM workflow.claim_candidates
            WHERE workflow_run_id = $1
        ), approved_batch AS (
            SELECT a.decision_identity, a.reviewer_subject, max(a.created_at) AS approved_at,
                   a.candidate_version
            FROM workflow.approvals a
            JOIN latest l ON l.candidate_version = a.candidate_version
            WHERE a.workflow_run_id = $1 AND a.decision = 'approved' AND a.target_state = 'approved'
            GROUP BY a.decision_identity, a.reviewer_subject, a.candidate_version
            HAVING count(*) = (SELECT count(*) FROM workflow.claim_candidates c WHERE c.workflow_run_id = $1 AND c.candidate_version = a.candidate_version)
               AND NOT EXISTS (
                   SELECT 1 FROM workflow.claim_candidates invalid
                   WHERE invalid.workflow_run_id = $1
                     AND invalid.candidate_version = a.candidate_version
                     AND invalid.deterministic_validation_status <> 'passed'
               )
        )
        SELECT r.id, r.version, r.state,
               b.decision_identity, b.reviewer_subject, b.approved_at,
               s.id, s.source_identity_key, s.title, s.canonical_url, s.source_type,
               c.id, c.source_record_id, c.candidate_ordinal, c.claim_text,
               c.structured_candidate::text, c.deterministic_validation_status,
               c.deterministic_validation_result::text
        FROM workflow.runs r
        JOIN (SELECT * FROM approved_batch ORDER BY approved_at DESC LIMIT 1) b ON b.candidate_version = (SELECT candidate_version FROM latest)
        JOIN workflow.source_records s ON s.workflow_run_id = r.id
        JOIN workflow.claim_candidates c ON c.workflow_run_id = r.id AND c.candidate_version = b.candidate_version
        JOIN workflow.approvals a ON a.workflow_run_id = r.id AND a.candidate_id = c.id AND a.decision_identity = b.decision_identity
        WHERE r.id = $1 AND r.state = 'publishing' AND a.decision = 'approved'
          AND c.deterministic_validation_status = 'passed'
        ORDER BY c.candidate_ordinal;
        """;

    public const string LockPublicationIdentitySql = """
        SELECT pg_advisory_xact_lock(hashtextextended($1, 0));
        """;

    public const string LockAssetIdentitySql = """
        SELECT pg_advisory_xact_lock(hashtextextended($1, 1));
        """;

    public const string LoadPublicationSql = """
        SELECT content_fingerprint, public_source_id, public_claim_ids
        FROM workflow.publications
        WHERE idempotency_key = $1;
        """;

    public const string InsertPublicationSql = """
        INSERT INTO workflow.publications (
            idempotency_key, content_fingerprint, workflow_run_id,
            workflow_run_version, public_source_id, public_claim_ids)
        VALUES ($1, $2, $3, $4, $5, $6);
        """;

    public const string InsertSourceSql = """
        INSERT INTO public.sources (source_type, title, url, doi, pmid, trial_id)
        VALUES ($1, $2, $3, $4, $5, $6) RETURNING id;
        """;

    public const string GetOrCreateAssetSql = """
        WITH inserted AS (
            INSERT INTO public.longevity_assets (slug, name, asset_type, short_summary)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (slug) DO NOTHING
            RETURNING id, name, asset_type
        )
        SELECT id, name, asset_type FROM inserted
        UNION ALL
        SELECT id, name, asset_type FROM public.longevity_assets WHERE slug = $1
        LIMIT 1;
        """;

    public const string InsertClaimSql = """
        INSERT INTO public.claims (asset_id, claim_text, claim_type, target_system, evidence_score, evidence_scoring_policy_id, hype_score, risk_score, plain_english_verdict)
        VALUES ($1, $2, $3, $4, $5, $6, NULL, NULL, $7) RETURNING id;
        """;

    public const string InsertEvidenceSql = """
        INSERT INTO public.claim_evidence (claim_id, source_id, evidence_direction, evidence_level, population, outcome_measured, effect_summary, limitations, relevance_score)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9);
        """;
}
