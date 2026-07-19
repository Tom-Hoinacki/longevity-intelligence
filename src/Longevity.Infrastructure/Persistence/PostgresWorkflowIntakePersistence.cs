using Longevity.Application.Contracts;
using Longevity.Application.SourceNormalization;
using Longevity.Domain.Workflow;
using Npgsql;

namespace Longevity.Infrastructure.Persistence;

public sealed class PostgresWorkflowIntakePersistence(NpgsqlDataSource dataSource) : IWorkflowIntakePersistence
{
    public async Task<WorkflowIntakeResult> CreateOrGetAsync(WorkflowIntakeRequest request, ScientificSourceNormalizationResult normalized, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            Guid runId;
            string state;
            int version;
            var inserted = false;
            await using (var insert = new NpgsqlCommand(WorkflowIntakePersistencePolicy.InsertRunSql, connection, transaction))
            {
                insert.Parameters.AddWithValue("p_workflow_type", request.WorkflowType);
                insert.Parameters.AddWithValue("p_idempotency_key", request.IdempotencyKey);
                await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    runId = reader.GetGuid(0); state = reader.GetString(1); version = reader.GetInt32(2); inserted = true;
                }
                else
                {
                    await reader.CloseAsync();
                    await using var load = new NpgsqlCommand(WorkflowIntakePersistencePolicy.LoadRunSql, connection, transaction);
                    load.Parameters.AddWithValue("p_workflow_type", request.WorkflowType);
                    load.Parameters.AddWithValue("p_idempotency_key", request.IdempotencyKey);
                    await using var existing = await load.ExecuteReaderAsync(cancellationToken);
                    if (!await existing.ReadAsync(cancellationToken)) throw new InvalidOperationException("The workflow idempotency record was not found after a conflict.");
                    runId = existing.GetGuid(0); state = existing.GetString(1); version = existing.GetInt32(2);
                }
            }

            if (inserted)
            {
                await using var source = new NpgsqlCommand(WorkflowIntakePersistencePolicy.InsertSourceSql, connection, transaction);
                AddSourceParameters(source, runId, request.Source, normalized);
                await source.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var loadSource = new NpgsqlCommand(WorkflowIntakePersistencePolicy.LoadSourceSql, connection, transaction);
                loadSource.Parameters.AddWithValue("p_workflow_run_id", runId);
                await using var sourceReader = await loadSource.ExecuteReaderAsync(cancellationToken);
                if (!await sourceReader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The existing workflow run has no normalized source.");
                var same = string.Equals(sourceReader.GetString(0), normalized.SourceIdentityKey, StringComparison.Ordinal)
                    && string.Equals(sourceReader.GetString(1), normalized.ContentHash, StringComparison.Ordinal);
                if (!same) throw new WorkflowIntakeConflictException("The idempotency key is already associated with different source content.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new WorkflowIntakeResult(new WorkflowRunId(runId), WorkflowState.FromDatabaseValue(state), version, !inserted);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddSourceParameters(NpgsqlCommand command, Guid runId, SubmittedAuthoritativeSource submitted, ScientificSourceNormalizationResult normalized)
    {
        var identifiers = normalized.SourceIdentityKey.Split(':', 2);
        command.Parameters.AddWithValue("p_workflow_run_id", runId);
        command.Parameters.AddWithValue("p_source_type", normalized.SourceType);
        command.Parameters.AddWithValue("p_identity", normalized.SourceIdentityKey);
        AddText(command, "p_canonical_url", normalized.CanonicalUrl);
        AddText(command, "p_doi", identifiers[0] == "doi" ? identifiers[1] : null);
        AddText(command, "p_pmid", identifiers[0] == "pmid" ? identifiers[1] : null);
        AddText(command, "p_clinicaltrials_id", identifiers[0] == "clinicaltrials" ? identifiers[1].ToUpperInvariant() : null);
        command.Parameters.AddWithValue("p_title", normalized.Title);
        command.Parameters.AddWithValue("p_normalized_text", normalized.NormalizedText);
        command.Parameters.AddWithValue("p_content_hash", normalized.ContentHash);
        command.Parameters.AddWithValue("p_normalization_version", normalized.NormalizationVersion);
    }

    private static void AddText(NpgsqlCommand command, string name, string? value) => command.Parameters.AddWithValue(name, NpgsqlTypes.NpgsqlDbType.Text, (object?)value ?? DBNull.Value);
}
