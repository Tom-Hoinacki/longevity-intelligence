using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;
using Npgsql;

namespace Longevity.Infrastructure.Persistence;

public sealed class PostgresClaimCandidateValidationPersistence(NpgsqlDataSource dataSource) : IClaimCandidateValidationPersistence
{
    public async Task<IReadOnlyList<ClaimCandidateForValidation>> LoadLatestCandidateBatchAsync(
        WorkflowRunId workflowRunId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ValidationPersistencePolicy.LoadLatestCandidateBatchSql);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlTypes.NpgsqlDbType.Uuid, workflowRunId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var candidates = new List<ClaimCandidateForValidation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new ClaimCandidateForValidation(
                new ClaimCandidateId(reader.GetGuid(0)),
                new WorkflowRunId(reader.GetGuid(1)),
                new SourceRecordId(reader.GetGuid(2)),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return candidates;
    }

    public async Task PersistValidationResultsAsync(
        WorkflowRunId workflowRunId,
        IReadOnlyList<ClaimCandidateValidationUpdate> updates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            throw new ArgumentException("At least one validation result is required.", nameof(updates));
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var update in updates)
            {
                await using var command = new NpgsqlCommand(ValidationPersistencePolicy.UpdateValidationResultSql, connection, transaction);
                command.Parameters.AddWithValue("p_candidate_id", NpgsqlTypes.NpgsqlDbType.Uuid, update.Candidate.CandidateId.Value);
                command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlTypes.NpgsqlDbType.Uuid, workflowRunId.Value);
                command.Parameters.AddWithValue("p_candidate_version", NpgsqlTypes.NpgsqlDbType.Integer, update.Candidate.CandidateVersion);
                command.Parameters.AddWithValue("p_candidate_ordinal", NpgsqlTypes.NpgsqlDbType.Integer, update.Candidate.CandidateOrdinal);
                command.Parameters.AddWithValue("p_validation_status", NpgsqlTypes.NpgsqlDbType.Text, update.Result.Passed ? DeterministicValidationStatus.Passed.DatabaseValue : DeterministicValidationStatus.Failed.DatabaseValue);
                command.Parameters.AddWithValue("p_validation_result", NpgsqlTypes.NpgsqlDbType.Jsonb, update.Result.ValidationResultJson);

                var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                {
                    throw new InvalidOperationException("Validation result update did not affect exactly one candidate.");
                }
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
