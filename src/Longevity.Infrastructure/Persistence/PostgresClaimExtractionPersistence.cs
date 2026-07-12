using System.Data;
using Longevity.Application.Contracts;
using Npgsql;

namespace Longevity.Infrastructure.Persistence;

public sealed class PostgresClaimExtractionPersistence(NpgsqlDataSource dataSource) : IClaimExtractionPersistence
{
    public async Task<NormalizedScientificSource?> LoadNormalizedSourceAsync(
        Longevity.Domain.Workflow.WorkflowRunId workflowRunId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ClaimExtractionPersistencePolicy.LoadNormalizedSourceSql);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlTypes.NpgsqlDbType.Uuid, workflowRunId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var source = new NormalizedScientificSource(
            new Longevity.Domain.Workflow.SourceRecordId(reader.GetGuid(0)),
            new Longevity.Domain.Workflow.WorkflowRunId(reader.GetGuid(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4));

        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The workflow run has multiple normalized source records.");
        }

        return source;
    }

    public async Task PersistExtractionAsync(
        ClaimExtractionPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Candidates.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var row in request.Candidates)
            {
                await using var command = new NpgsqlCommand(ClaimExtractionPersistencePolicy.InsertClaimCandidateSql, connection, transaction);
                command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlTypes.NpgsqlDbType.Uuid, row.WorkflowRunId.Value);
                command.Parameters.AddWithValue("p_source_record_id", NpgsqlTypes.NpgsqlDbType.Uuid, row.SourceRecordId.Value);
                command.Parameters.AddWithValue("p_candidate_version", NpgsqlTypes.NpgsqlDbType.Integer, row.CandidateVersion);
                command.Parameters.AddWithValue("p_candidate_ordinal", NpgsqlTypes.NpgsqlDbType.Integer, row.CandidateOrdinal);
                command.Parameters.AddWithValue("p_schema_version", NpgsqlTypes.NpgsqlDbType.Text, row.Metadata.SchemaVersion);
                command.Parameters.AddWithValue("p_claim_text", NpgsqlTypes.NpgsqlDbType.Text, row.Candidate.ClaimText);
                command.Parameters.AddWithValue("p_structured_candidate", NpgsqlTypes.NpgsqlDbType.Jsonb, row.Candidate.StructuredCandidateJson);
                command.Parameters.AddWithValue("p_model_provider", NpgsqlTypes.NpgsqlDbType.Text, row.Metadata.ModelProvider);
                command.Parameters.AddWithValue("p_model_name", NpgsqlTypes.NpgsqlDbType.Text, row.Metadata.ModelName);
                command.Parameters.AddWithValue("p_prompt_version", NpgsqlTypes.NpgsqlDbType.Text, row.Metadata.PromptVersion);
                command.Parameters.AddWithValue("p_input_token_count", NpgsqlTypes.NpgsqlDbType.Integer, (object?)row.Metadata.InputTokenCount ?? DBNull.Value);
                command.Parameters.AddWithValue("p_output_token_count", NpgsqlTypes.NpgsqlDbType.Integer, (object?)row.Metadata.OutputTokenCount ?? DBNull.Value);
                command.Parameters.AddWithValue("p_estimated_cost", NpgsqlTypes.NpgsqlDbType.Numeric, (object?)row.Metadata.EstimatedCost ?? DBNull.Value);
                command.Parameters.AddWithValue("p_latency_ms", NpgsqlTypes.NpgsqlDbType.Integer, (object?)row.Metadata.LatencyMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue("p_trace_identifier", NpgsqlTypes.NpgsqlDbType.Text, (object?)row.Metadata.TraceIdentifier ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
