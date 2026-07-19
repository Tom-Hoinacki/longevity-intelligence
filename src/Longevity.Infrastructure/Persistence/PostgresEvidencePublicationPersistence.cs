using Longevity.Application.Contracts;
using Longevity.Application.Publishing;
using Longevity.Application.Validation;
using Longevity.Domain.Workflow;
using Npgsql;
using NpgsqlTypes;

namespace Longevity.Infrastructure.Persistence;

public sealed class PostgresEvidencePublicationPersistence(NpgsqlDataSource dataSource) : IEvidencePublicationPersistence
{
    public async Task<ApprovedPublicationBatch?> LoadApprovedPublicationBatchAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(PublicationPersistencePolicy.LoadBatchSql);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlDbType.Uuid, workflowRunId.Value);
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
            var validationPassed = string.Equals(reader.GetString(16), "passed", StringComparison.Ordinal);
            claims.Add(new PublicationClaim(
                new ClaimCandidateId(reader.GetGuid(11)),
                workflowRunId,
                new SourceRecordId(reader.GetGuid(12)),
                reader.GetInt32(13),
                reader.GetString(14),
                reader.GetString(15),
                validationPassed,
                humanApproved: true,
                reader.GetString(17)));
        } while (await reader.ReadAsync(cancellationToken));

        var evidence = claims.Select(claim => new PublicationEvidenceLink(claim.CandidateId, source.SourceRecordId, "primary_source")).ToArray();
        return new ApprovedPublicationBatch(workflowRunId, workflowVersion, workflowState, approvalId, approvedAt, reviewer, source, claims, evidence);
    }

    public async Task<AtomicPublicationResult> PublishAtomicallyAsync(AtomicPublicationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var preparedClaims = PrepareClaims(command.Batch.Claims);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await Execute(connection, transaction, PublicationPersistencePolicy.LockPublicationIdentitySql, cancellationToken,
                sql => sql.Parameters.AddWithValue("p_idempotency_key", NpgsqlDbType.Text, command.IdempotencyKey));

            await using (var existing = new NpgsqlCommand(PublicationPersistencePolicy.LoadPublicationSql, connection, transaction))
            {
                existing.Parameters.AddWithValue("p_idempotency_key", NpgsqlDbType.Text, command.IdempotencyKey);
                await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var fingerprint = reader.GetString(0);
                    await reader.CloseAsync();
                    if (!string.Equals(fingerprint, command.ContentFingerprint, StringComparison.Ordinal))
                        throw new PublicationConflictException("The publication idempotency key is associated with different content.");
                    await transaction.CommitAsync(cancellationToken);
                    return AtomicPublicationResult.AlreadyPublishedIdentically;
                }
            }

            var sourceId = await ScalarGuid(connection, transaction, PublicationPersistencePolicy.InsertSourceSql, cancellationToken, sql =>
            {
                var identifiers = command.Batch.Source.IdentityKey.Split(':', 2);
                sql.Parameters.AddWithValue("p_source_type", NpgsqlDbType.Text, command.Batch.Source.SourceType);
                sql.Parameters.AddWithValue("p_title", NpgsqlDbType.Text, command.Batch.Source.Title);
                AddText(sql, "p_url", command.Batch.Source.CanonicalUrl);
                AddText(sql, "p_doi", identifiers[0] == "doi" ? identifiers[1] : null);
                AddText(sql, "p_pmid", identifiers[0] == "pmid" ? identifiers[1] : null);
                AddText(sql, "p_trial_id", identifiers[0] == "clinicaltrials" ? identifiers[1].ToUpperInvariant() : null);
            });

            var publicClaimIds = new List<Guid>(preparedClaims.Count);
            foreach (var prepared in preparedClaims)
            {
                var assetId = await GetOrCreateAssetAsync(connection, transaction, prepared.Structured, cancellationToken);
                var claimId = await ScalarGuid(connection, transaction, PublicationPersistencePolicy.InsertClaimSql, cancellationToken, sql =>
                {
                    sql.Parameters.AddWithValue("p_asset_id", NpgsqlDbType.Uuid, assetId);
                    sql.Parameters.AddWithValue("p_claim_text", NpgsqlDbType.Text, prepared.Claim.ClaimText);
                    AddText(sql, "p_claim_type", prepared.Structured.ClaimType);
                    AddText(sql, "p_target_system", prepared.Structured.TargetSystem);
                    AddNumeric(sql, "p_evidence_score", prepared.Scoring.PublicScore);
                    sql.Parameters.AddWithValue("p_scoring_policy_id", NpgsqlDbType.Text, prepared.Scoring.PolicyId);
                    sql.Parameters.AddWithValue("p_verdict", NpgsqlDbType.Text, PublicVerdict(prepared.Scoring.Verdict));
                });
                publicClaimIds.Add(claimId);

                await Execute(connection, transaction, PublicationPersistencePolicy.InsertEvidenceSql, cancellationToken, sql =>
                {
                    sql.Parameters.AddWithValue("p_claim_id", NpgsqlDbType.Uuid, claimId);
                    sql.Parameters.AddWithValue("p_source_id", NpgsqlDbType.Uuid, sourceId);
                    sql.Parameters.AddWithValue("p_direction", NpgsqlDbType.Text, prepared.Structured.EvidenceDirection);
                    sql.Parameters.AddWithValue("p_level", NpgsqlDbType.Text, prepared.Structured.EvidenceLevel);
                    AddText(sql, "p_population", prepared.Structured.Population);
                    AddText(sql, "p_outcome", prepared.Structured.OutcomeMeasured);
                    AddText(sql, "p_effect", prepared.Structured.EffectSummary);
                    sql.Parameters.AddWithValue("p_limitations", NpgsqlDbType.Text, prepared.Structured.Limitations);
                    AddNumeric(sql, "p_relevance", Relevance(prepared.Structured.Directness));
                });
            }

            await Execute(connection, transaction, PublicationPersistencePolicy.InsertPublicationSql, cancellationToken, sql =>
            {
                sql.Parameters.AddWithValue("p_idempotency_key", NpgsqlDbType.Text, command.IdempotencyKey);
                sql.Parameters.AddWithValue("p_content_fingerprint", NpgsqlDbType.Text, command.ContentFingerprint);
                sql.Parameters.AddWithValue("p_workflow_run_id", NpgsqlDbType.Uuid, command.Batch.WorkflowRunId.Value);
                sql.Parameters.AddWithValue("p_workflow_run_version", NpgsqlDbType.Integer, command.Batch.WorkflowRunVersion);
                sql.Parameters.AddWithValue("p_public_source_id", NpgsqlDbType.Uuid, sourceId);
                sql.Parameters.AddWithValue("p_public_claim_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, publicClaimIds.ToArray());
            });

            await transaction.CommitAsync(cancellationToken);
            return AtomicPublicationResult.NewlyPublished;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static IReadOnlyList<PreparedClaim> PrepareClaims(IReadOnlyList<PublicationClaim> claims)
    {
        var result = new List<PreparedClaim>(claims.Count);
        foreach (var claim in claims)
        {
            if (!StructuredClaimCandidateParser.TryParse(claim.StructuredCandidateJson, out var structured, out var errors))
                throw new PublicationInvariantException($"Candidate {claim.CandidateId.Value:N} failed publication parsing: {string.Join(',', errors)}");
            if (!DeterministicValidationArtifactParser.TryReadScoring(claim.DeterministicValidationJson, out var scoring))
                throw new PublicationInvariantException($"Candidate {claim.CandidateId.Value:N} has no deterministic scoring artifact.");
            if (!string.Equals(scoring!.Alignment, structured!.EvidenceDirection, StringComparison.Ordinal))
                throw new PublicationInvariantException($"Candidate {claim.CandidateId.Value:N} has contradictory scoring alignment.");
            result.Add(new(claim, structured, scoring));
        }
        return result;
    }

    private static async Task<Guid> GetOrCreateAssetAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, StructuredClaimCandidate structured, CancellationToken cancellationToken)
    {
        await Execute(connection, transaction, PublicationPersistencePolicy.LockAssetIdentitySql, cancellationToken,
            sql => sql.Parameters.AddWithValue("p_asset_slug", NpgsqlDbType.Text, structured.AssetSlug));

        await using var command = new NpgsqlCommand(PublicationPersistencePolicy.GetOrCreateAssetSql, connection, transaction);
        command.Parameters.AddWithValue("p_slug", NpgsqlDbType.Text, structured.AssetSlug);
        command.Parameters.AddWithValue("p_name", NpgsqlDbType.Text, structured.AssetName);
        command.Parameters.AddWithValue("p_type", NpgsqlDbType.Text, structured.AssetType);
        AddText(command, "p_summary", structured.AssetSummary);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The publication asset insert returned no record.");
        if (!string.Equals(reader.GetString(1), structured.AssetName, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(2), structured.AssetType, StringComparison.Ordinal))
            throw new PublicationConflictException("The proposed asset conflicts with an existing public asset.");
        return reader.GetGuid(0);
    }

    private static string PublicVerdict(string verdict) => $"Deterministic evidence-policy classification: {verdict}. This is not a medically validated grade.";
    private static decimal Relevance(string directness) => directness switch { "indirect" => 1.5m, "partially_direct" => 3.0m, "direct" => 5.0m, _ => throw new ArgumentOutOfRangeException(nameof(directness)) };
    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token, Action<NpgsqlCommand> configure)
    { await using var command = new NpgsqlCommand(sql, connection, transaction); configure(command); await command.ExecuteNonQueryAsync(token); }
    private static async Task<Guid> ScalarGuid(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token, Action<NpgsqlCommand> configure)
    { await using var command = new NpgsqlCommand(sql, connection, transaction); configure(command); var value = await command.ExecuteScalarAsync(token); return value is Guid id ? id : throw new InvalidOperationException("The publication insert did not return an id."); }
    private static void AddNumeric(NpgsqlCommand command, string name, decimal value) => command.Parameters.AddWithValue(name, NpgsqlDbType.Numeric, value);
    private static void AddText(NpgsqlCommand command, string name, string? value) => command.Parameters.AddWithValue(name, NpgsqlDbType.Text, (object?)value ?? DBNull.Value);

    private sealed record PreparedClaim(PublicationClaim Claim, StructuredClaimCandidate Structured, DeterministicScoringArtifact Scoring);
}
