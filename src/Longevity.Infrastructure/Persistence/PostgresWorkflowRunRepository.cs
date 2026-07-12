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
            WorkflowState.FromDatabaseValue(reader.GetString(1)));
    }
}
