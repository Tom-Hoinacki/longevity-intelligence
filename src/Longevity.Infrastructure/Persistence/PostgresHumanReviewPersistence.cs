using System.Data;
using Longevity.Application.HumanReview;
using Longevity.Domain.Workflow;
using Npgsql;
using NpgsqlTypes;

namespace Longevity.Infrastructure.Persistence;

public sealed class PostgresHumanReviewPersistence(NpgsqlDataSource dataSource) : IHumanReviewPersistence
{
    public async Task<PendingHumanReviewBatch?> LoadPendingAsync(
        WorkflowRunId workflowRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(HumanReviewPersistencePolicy.LoadPendingSql);
            command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlDbType.Uuid, workflowRunId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

            var loadedRunId = new WorkflowRunId(reader.GetGuid(0));
            var workflowVersion = reader.GetInt32(1);
            var state = WorkflowState.FromDatabaseValue(reader.GetString(2));
            if (reader.IsDBNull(3)) throw new HumanReviewDataIntegrityException();

            var candidates = new List<PendingHumanReviewCandidate>();
            do
            {
                candidates.Add(ReadPendingCandidate(reader, loadedRunId, 3));
            }
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

            return CreateBatch(loadedRunId, workflowVersion, state, candidates);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HumanReviewDataIntegrityException)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            throw new HumanReviewPersistenceException();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException or InvalidOperationException)
        {
            throw new HumanReviewDataIntegrityException();
        }
    }

    public async Task<StoredHumanReviewDecision?> LoadDecisionAsync(
        string decisionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decisionId)) throw new ArgumentException("Decision identity is required.", nameof(decisionId));

        try
        {
            await using var command = dataSource.CreateCommand(HumanReviewPersistencePolicy.LoadDecisionSql);
            command.Parameters.AddWithValue("p_decision_identity", NpgsqlDbType.Text, decisionId.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadStoredDecisionAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HumanReviewDataIntegrityException)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            throw new HumanReviewPersistenceException();
        }
    }

    public async Task<bool> WorkflowRunExistsAsync(
        WorkflowRunId workflowRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(HumanReviewPersistencePolicy.WorkflowRunExistsSql);
            command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlDbType.Uuid, workflowRunId.Value);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new HumanReviewDataIntegrityException());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HumanReviewDataIntegrityException)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            throw new HumanReviewPersistenceException();
        }
    }

    public async Task<HumanReviewDecisionResult> AppendDecisionAsync(
        HumanReviewPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AcquireDecisionLockAsync(connection, transaction, request.DecisionId, cancellationToken).ConfigureAwait(false);

            var existing = await LoadDecisionAsync(connection, transaction, request.DecisionId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!existing.Matches(
                        request.WorkflowRunId,
                        request.Decision,
                        request.ReviewerIdentity,
                        request.Reason,
                        request.Note,
                        request.TargetState))
                    throw new HumanReviewConflictException();

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return existing.ToResult();
            }

            await VerifyAndLockRunAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            var candidates = await LoadEligibleCandidatesAsync(connection, transaction, request.WorkflowRunId, cancellationToken).ConfigureAwait(false);

            foreach (var candidate in candidates)
                await InsertApprovalAsync(connection, transaction, request, candidate, cancellationToken).ConfigureAwait(false);

            await TransitionRunAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new HumanReviewDecisionResult(
                request.WorkflowRunId,
                request.DecisionId,
                request.Decision,
                request.TargetState,
                request.DecisionAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackSafelyAsync(transaction).ConfigureAwait(false);
            throw;
        }
        catch (HumanReviewConflictException)
        {
            await RollbackSafelyAsync(transaction).ConfigureAwait(false);
            throw;
        }
        catch (HumanReviewDataIntegrityException)
        {
            await RollbackSafelyAsync(transaction).ConfigureAwait(false);
            throw;
        }
        catch (NpgsqlException)
        {
            await RollbackSafelyAsync(transaction).ConfigureAwait(false);
            throw new HumanReviewPersistenceException();
        }
        catch
        {
            await RollbackSafelyAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            throw new HumanReviewPersistenceException();
        }
    }

    private static PendingHumanReviewCandidate ReadPendingCandidate(
        NpgsqlDataReader reader,
        WorkflowRunId workflowRunId,
        int offset)
    {
        if (!string.Equals(reader.GetString(offset + 6), "passed", StringComparison.Ordinal))
            throw new HumanReviewDataIntegrityException();

        return new PendingHumanReviewCandidate(
            new ClaimCandidateId(reader.GetGuid(offset)),
            workflowRunId,
            new SourceRecordId(reader.GetGuid(offset + 1)),
            reader.GetInt32(offset + 2),
            reader.GetInt32(offset + 3),
            reader.GetString(offset + 4),
            reader.GetString(offset + 5),
            new DeterministicValidationSnapshot(true, reader.GetString(offset + 7)));
    }

    private static PendingHumanReviewBatch CreateBatch(
        WorkflowRunId workflowRunId,
        int version,
        WorkflowState state,
        IReadOnlyList<PendingHumanReviewCandidate> candidates)
    {
        try
        {
            return new PendingHumanReviewBatch(workflowRunId, version, state, candidates);
        }
        catch (ArgumentException)
        {
            throw new HumanReviewDataIntegrityException();
        }
    }

    private static async Task<StoredHumanReviewDecision?> ReadStoredDecisionAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        try
        {
            var decision = new StoredHumanReviewDecision(
                new WorkflowRunId(reader.GetGuid(0)),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                ParseDecision(reader.GetString(4)),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                WorkflowState.FromDatabaseValue(reader.GetString(9)));

            if (reader.GetInt32(10) < 1 || reader.GetInt32(10) != reader.GetInt32(11))
                throw new HumanReviewDataIntegrityException();
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new HumanReviewDataIntegrityException();

            return decision;
        }
        catch (HumanReviewDataIntegrityException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException or InvalidOperationException)
        {
            throw new HumanReviewDataIntegrityException();
        }
    }

    private static async Task AcquireDecisionLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string decisionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(HumanReviewPersistencePolicy.LockDecisionIdentitySql, connection, transaction);
        command.Parameters.AddWithValue("p_decision_identity", NpgsqlDbType.Text, decisionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StoredHumanReviewDecision?> LoadDecisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string decisionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(HumanReviewPersistencePolicy.LoadDecisionSql, connection, transaction);
        command.Parameters.AddWithValue("p_decision_identity", NpgsqlDbType.Text, decisionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadStoredDecisionAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyAndLockRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HumanReviewPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(HumanReviewPersistencePolicy.LockWorkflowRunSql, connection, transaction);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlDbType.Uuid, request.WorkflowRunId.Value);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || !string.Equals(reader.GetString(0), WorkflowState.AwaitingHumanApproval.DatabaseValue, StringComparison.Ordinal)
            || reader.GetInt32(1) != request.ExpectedWorkflowVersion)
            throw new HumanReviewConflictException();
    }

    private static async Task<IReadOnlyList<PendingHumanReviewCandidate>> LoadEligibleCandidatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WorkflowRunId workflowRunId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(HumanReviewPersistencePolicy.LoadEligibleCandidateBatchSql, connection, transaction);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlDbType.Uuid, workflowRunId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<PendingHumanReviewCandidate>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(reader.GetString(7), "passed", StringComparison.Ordinal))
                throw new HumanReviewDataIntegrityException();
            try
            {
                candidates.Add(new PendingHumanReviewCandidate(
                    new ClaimCandidateId(reader.GetGuid(0)),
                    new WorkflowRunId(reader.GetGuid(1)),
                    new SourceRecordId(reader.GetGuid(2)),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    new DeterministicValidationSnapshot(true, reader.GetString(8))));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidCastException or InvalidOperationException)
            {
                throw new HumanReviewDataIntegrityException();
            }
        }

        return CreateBatch(workflowRunId, 1, WorkflowState.AwaitingHumanApproval, candidates).Candidates;
    }

    private static async Task InsertApprovalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HumanReviewPersistenceRequest request,
        PendingHumanReviewCandidate candidate,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(HumanReviewPersistencePolicy.InsertApprovalSql, connection, transaction);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlDbType.Uuid, request.WorkflowRunId.Value);
        command.Parameters.AddWithValue("p_candidate_id", NpgsqlDbType.Uuid, candidate.CandidateId.Value);
        command.Parameters.AddWithValue("p_candidate_version", NpgsqlDbType.Integer, candidate.CandidateVersion);
        command.Parameters.AddWithValue("p_decision", NpgsqlDbType.Text, ToDatabaseDecision(request.Decision));
        command.Parameters.AddWithValue("p_reviewer_subject", NpgsqlDbType.Text, request.ReviewerIdentity);
        command.Parameters.AddWithValue("p_rationale", NpgsqlDbType.Text, (object?)request.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("p_created_at", NpgsqlDbType.TimestampTz, request.DecisionAt);
        command.Parameters.AddWithValue("p_decision_identity", NpgsqlDbType.Text, request.DecisionId);
        command.Parameters.AddWithValue("p_expected_workflow_version", NpgsqlDbType.Integer, request.ExpectedWorkflowVersion);
        command.Parameters.AddWithValue("p_target_state", NpgsqlDbType.Text, request.TargetState.DatabaseValue);
        command.Parameters.AddWithValue("p_reviewer_note", NpgsqlDbType.Text, (object?)request.Note ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new HumanReviewPersistenceException();
    }

    private static async Task TransitionRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HumanReviewPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(HumanReviewPersistencePolicy.TransitionWorkflowRunSql, connection, transaction);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlDbType.Uuid, request.WorkflowRunId.Value);
        command.Parameters.AddWithValue("p_expected_version", NpgsqlDbType.Integer, request.ExpectedWorkflowVersion);
        command.Parameters.AddWithValue("p_expected_state", NpgsqlDbType.Text, WorkflowState.AwaitingHumanApproval.DatabaseValue);
        command.Parameters.AddWithValue("p_target_state", NpgsqlDbType.Text, request.TargetState.DatabaseValue);
        command.Parameters.AddWithValue("p_decision_at", NpgsqlDbType.TimestampTz, request.DecisionAt);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new HumanReviewConflictException();
        if (reader.GetGuid(0) != request.WorkflowRunId.Value
            || !string.Equals(reader.GetString(1), request.TargetState.DatabaseValue, StringComparison.Ordinal)
            || reader.GetInt32(2) != request.ExpectedWorkflowVersion + 1)
            throw new HumanReviewDataIntegrityException();
    }

    private static void ValidateRequest(HumanReviewPersistenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkflowRunId.Value == Guid.Empty
            || request.ExpectedWorkflowVersion < 1
            || string.IsNullOrWhiteSpace(request.DecisionId)
            || string.IsNullOrWhiteSpace(request.ReviewerIdentity))
            throw new ArgumentException("Human-review persistence request is invalid.", nameof(request));
        if (request.Decision == HumanReviewDecision.Reject && string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A rejection reason is required.", nameof(request));
        if ((request.Decision == HumanReviewDecision.Approve && request.TargetState != WorkflowState.Approved)
            || (request.Decision == HumanReviewDecision.Reject && request.TargetState != WorkflowState.Rejected))
            throw new ArgumentException("Human-review decision and target state do not match.", nameof(request));
    }

    private static HumanReviewDecision ParseDecision(string value) => value switch
    {
        "approved" => HumanReviewDecision.Approve,
        "rejected" => HumanReviewDecision.Reject,
        _ => throw new HumanReviewDataIntegrityException()
    };

    private static string ToDatabaseDecision(HumanReviewDecision decision) => decision switch
    {
        HumanReviewDecision.Approve => "approved",
        HumanReviewDecision.Reject => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(decision))
    };

    private static async Task RollbackSafelyAsync(NpgsqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The original sanitized failure remains authoritative.
        }
    }
}
