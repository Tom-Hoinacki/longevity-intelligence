using Longevity.Domain.Workflow;
using Longevity.Application.SourceNormalization;

namespace Longevity.Application.Contracts;

public sealed record WorkflowIntakeRequest(string IdempotencyKey, SubmittedAuthoritativeSource Source, string WorkflowType);
public sealed record WorkflowIntakeResult(WorkflowRunId WorkflowRunId, WorkflowState State, int Version, bool AlreadyExisted);

public interface IWorkflowIntakePersistence
{
    Task<WorkflowIntakeResult> CreateOrGetAsync(WorkflowIntakeRequest request, ScientificSourceNormalizationResult normalized, CancellationToken cancellationToken);
}

public interface IWorkflowIntakeService
{
    Task<WorkflowIntakeResult> IntakeAsync(WorkflowIntakeRequest request, CancellationToken cancellationToken);
}

public sealed class WorkflowIntakeConflictException(string message) : InvalidOperationException(message);
public sealed class WorkflowIntakeUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);
