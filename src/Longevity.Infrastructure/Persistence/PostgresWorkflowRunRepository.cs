using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;
using Npgsql;
using System.Data;

namespace Longevity.Infrastructure.Persistence;

public sealed class PostgresWorkflowRunRepository(NpgsqlDataSource dataSource) : IWorkflowRunRepository
{
    public async Task<ClaimedWorkflowRun?> TryClaimNextRunnableAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(WorkflowRunClaimPolicy.ClaimNextRunnableSql);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClaimedWorkflowRun(
            new WorkflowRunId(reader.GetGuid(0)),
            WorkflowState.FromDatabaseValue(reader.GetString(1)),
            reader.GetInt32(2));
    }

    public async Task<WorkflowRunCompletionResult> CompleteClaimedPhaseAsync(
        WorkflowRunId workflowRunId,
        WorkflowState expectedCurrentState,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var targetState = WorkflowRunClaimPolicy.GetCompletionTarget(expectedCurrentState);
        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "Expected workflow version must be at least 1.");
        }

        await using var command = dataSource.CreateCommand(WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlTypes.NpgsqlDbType.Uuid, workflowRunId.Value);
        command.Parameters.AddWithValue("p_expected_state", NpgsqlTypes.NpgsqlDbType.Text, expectedCurrentState.DatabaseValue);
        command.Parameters.AddWithValue("p_expected_version", NpgsqlTypes.NpgsqlDbType.Integer, expectedVersion);
        command.Parameters.AddWithValue("p_target_state", NpgsqlTypes.NpgsqlDbType.Text, targetState);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return WorkflowRunCompletionResult.Conflict(workflowRunId);
        }

        return new WorkflowRunCompletionResult(
            WorkflowRunCompletionStatus.Completed,
            new WorkflowRunId(reader.GetGuid(0)),
            WorkflowState.FromDatabaseValue(reader.GetString(1)),
            reader.GetInt32(2));
    }

    public async Task<WorkflowRunFailureResult> FailClaimedPhaseAsync(
        WorkflowRunId workflowRunId,
        WorkflowState expectedCurrentState,
        int expectedVersion,
        DateTimeOffset retryAt,
        string? sanitizedFailureSummary,
        CancellationToken cancellationToken)
    {
        var retryTargetState = WorkflowRunClaimPolicy.GetFailureRetryTarget(expectedCurrentState);
        var terminalTargetState = WorkflowRunClaimPolicy.GetFailureTerminalTarget(expectedCurrentState);

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "Expected workflow version must be at least 1.");
        }

        await using var command = dataSource.CreateCommand(WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        command.Parameters.AddWithValue("p_workflow_run_id", NpgsqlTypes.NpgsqlDbType.Uuid, workflowRunId.Value);
        command.Parameters.AddWithValue("p_expected_state", NpgsqlTypes.NpgsqlDbType.Text, expectedCurrentState.DatabaseValue);
        command.Parameters.AddWithValue("p_expected_version", NpgsqlTypes.NpgsqlDbType.Integer, expectedVersion);
        command.Parameters.AddWithValue("p_retry_target_state", NpgsqlTypes.NpgsqlDbType.Text, retryTargetState);
        command.Parameters.AddWithValue("p_terminal_target_state", NpgsqlTypes.NpgsqlDbType.Text, terminalTargetState);
        command.Parameters.AddWithValue("p_retry_at", NpgsqlTypes.NpgsqlDbType.TimestampTz, retryAt);
        command.Parameters.AddWithValue("p_sanitized_failure_summary", NpgsqlTypes.NpgsqlDbType.Text, (object?)sanitizedFailureSummary ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return WorkflowRunFailureResult.Conflict(workflowRunId);
        }

        var state = WorkflowState.FromDatabaseValue(reader.GetString(1));
        var version = reader.GetInt32(2);
        var retryCount = reader.GetInt32(3);

        return new WorkflowRunFailureResult(
            state.DatabaseValue == terminalTargetState
                ? WorkflowRunFailureStatus.TerminalFailure
                : WorkflowRunFailureStatus.RetryScheduled,
            new WorkflowRunId(reader.GetGuid(0)),
            state,
            version,
            retryCount);
    }
}
