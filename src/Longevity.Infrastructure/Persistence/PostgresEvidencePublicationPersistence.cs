using Longevity.Application.Contracts;
using Longevity.Application.Publishing;
using Longevity.Domain.Workflow;
using Npgsql;

namespace Longevity.Infrastructure.Persistence;

public sealed class PostgresEvidencePublicationPersistence(NpgsqlDataSource dataSource) : IEvidencePublicationPersistence
{
    public async Task<ApprovedPublicationBatch?> LoadApprovedPublicationBatchAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(PublicationPersistencePolicy.LoadBatchSql);
        command.Parameters.AddWithValue("p_workflow_run_id", workflowRunId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var workflowVersion = reader.GetInt32(1);
        var workflowState = WorkflowState.FromDatabaseValue(reader.GetString(2));
        var approvalId = reader.GetString(3);
        var reviewer = reader.GetString(4);
        var approvedAt = reader.GetFieldValue<DateTimeOffset>(5);
        var source = new PublicationSource(new SourceRecordId(reader.GetGuid(6)), workflowRunId, reader.GetString(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10));
        var claims = new List<PublicationClaim>();
        do
        {
            claims.Add(new PublicationClaim(new ClaimCandidateId(reader.GetGuid(11)), workflowRunId, new SourceRecordId(reader.GetGuid(12)), reader.GetInt32(13), reader.GetString(14), reader.GetString(15), true, true));
        } while (await reader.ReadAsync(cancellationToken));

        var evidence = claims.Select(claim => new PublicationEvidenceLink(claim.CandidateId, source.SourceRecordId, "primary_source")).ToArray();
        return new ApprovedPublicationBatch(workflowRunId, workflowVersion, workflowState, approvalId, approvedAt, reviewer, source, claims, evidence);
    }

    public async Task<AtomicPublicationResult> PublishAtomicallyAsync(AtomicPublicationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var existing = new NpgsqlCommand(PublicationPersistencePolicy.LoadPublicationSql, connection, transaction))
            {
                existing.Parameters.AddWithValue("p_idempotency_key", command.IdempotencyKey);
                await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var fingerprint = reader.GetString(0);
                    await reader.CloseAsync();
                    if (!string.Equals(fingerprint, command.ContentFingerprint, StringComparison.Ordinal)) throw new PublicationConflictException("The publication idempotency key is associated with different content.");
                    await transaction.CommitAsync(cancellationToken);
                    return AtomicPublicationResult.AlreadyPublishedIdentically;
                }
            }

            await Execute(connection, transaction, PublicationPersistencePolicy.InsertPublicationSql, cancellationToken, (c) =>
            {
                c.Parameters.AddWithValue("p_idempotency_key", command.IdempotencyKey);
                c.Parameters.AddWithValue("p_content_fingerprint", command.ContentFingerprint);
                c.Parameters.AddWithValue("p_workflow_run_id", command.Batch.WorkflowRunId.Value);
                c.Parameters.AddWithValue("p_workflow_run_version", command.Batch.WorkflowRunVersion);
            });

            var sourceId = await ScalarGuid(connection, transaction, PublicationPersistencePolicy.InsertSourceSql, cancellationToken, c =>
            {
                var identifiers = command.Batch.Source.IdentityKey.Split(':', 2);
                c.Parameters.AddWithValue("p_source_type", command.Batch.Source.SourceType);
                c.Parameters.AddWithValue("p_title", command.Batch.Source.Title);
                AddText(c, "p_url", command.Batch.Source.CanonicalUrl);
                AddText(c, "p_doi", identifiers[0] == "doi" ? identifiers[1] : null);
                AddText(c, "p_pmid", identifiers[0] == "pmid" ? identifiers[1] : null);
                AddText(c, "p_trial_id", identifiers[0] == "clinicaltrials" ? identifiers[1].ToUpperInvariant() : null);
            });

            foreach (var claim in command.Batch.Claims)
            {
                if (!StructuredClaimCandidateParser.TryParse(claim.StructuredCandidateJson, out var structured, out var errors)) throw new PublicationInvariantException($"Candidate {claim.CandidateId.Value:N} failed publication parsing: {string.Join(',', errors)}");
                var assetId = await ScalarGuid(connection, transaction, PublicationPersistencePolicy.UpsertAssetSql, cancellationToken, c =>
                {
                    c.Parameters.AddWithValue("p_slug", structured!.AssetSlug); c.Parameters.AddWithValue("p_name", structured.AssetName); c.Parameters.AddWithValue("p_type", structured.AssetType); AddText(c, "p_summary", structured.AssetSummary);
                });
                var claimId = await ScalarGuid(connection, transaction, PublicationPersistencePolicy.InsertClaimSql, cancellationToken, c =>
                {
                    c.Parameters.AddWithValue("p_asset_id", assetId); c.Parameters.AddWithValue("p_claim_text", claim.ClaimText); AddText(c, "p_claim_type", structured!.ClaimType); AddText(c, "p_target_system", structured.TargetSystem); AddNumeric(c, "p_evidence_score", structured.EvidenceScore); AddNumeric(c, "p_hype_score", structured.HypeScore); AddNumeric(c, "p_risk_score", structured.RiskScore); AddText(c, "p_verdict", structured.PlainEnglishVerdict);
                });
                await Execute(connection, transaction, PublicationPersistencePolicy.InsertEvidenceSql, cancellationToken, c =>
                {
                    c.Parameters.AddWithValue("p_claim_id", claimId); c.Parameters.AddWithValue("p_source_id", sourceId); c.Parameters.AddWithValue("p_direction", structured!.EvidenceDirection); c.Parameters.AddWithValue("p_level", structured.EvidenceLevel); AddText(c, "p_population", structured.Population); AddText(c, "p_outcome", structured.OutcomeMeasured); AddText(c, "p_effect", structured.EffectSummary); c.Parameters.AddWithValue("p_limitations", structured.Limitations); AddNumeric(c, "p_relevance", structured.RelevanceScore);
                });
            }

            await transaction.CommitAsync(cancellationToken);
            return AtomicPublicationResult.NewlyPublished;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token, Action<NpgsqlCommand> configure)
    { await using var command = new NpgsqlCommand(sql, connection, transaction); configure(command); await command.ExecuteNonQueryAsync(token); }
    private static async Task<Guid> ScalarGuid(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token, Action<NpgsqlCommand> configure)
    { await using var command = new NpgsqlCommand(sql, connection, transaction); configure(command); var value = await command.ExecuteScalarAsync(token); return value is Guid id ? id : throw new InvalidOperationException("The publication insert did not return an id."); }
    private static void AddNumeric(NpgsqlCommand command, string name, decimal? value) => command.Parameters.AddWithValue(name, NpgsqlTypes.NpgsqlDbType.Numeric, (object?)value ?? DBNull.Value);
    private static void AddText(NpgsqlCommand command, string name, string? value) => command.Parameters.AddWithValue(name, NpgsqlTypes.NpgsqlDbType.Text, (object?)value ?? DBNull.Value);
}
