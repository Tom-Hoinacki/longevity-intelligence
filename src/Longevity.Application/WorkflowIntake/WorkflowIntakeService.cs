using Longevity.Application.Contracts;

namespace Longevity.Application.WorkflowIntake;

public sealed class WorkflowIntakeService(ISourceNormalizer normalizer, IWorkflowIntakePersistence persistence) : IWorkflowIntakeService
{
    public async Task<WorkflowIntakeResult> IntakeAsync(WorkflowIntakeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 200)
            throw new ArgumentException("The idempotency key must be non-empty and at most 200 characters.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkflowType) || request.WorkflowType.Trim().Length > 100)
            throw new ArgumentException("The workflow type must be non-empty and at most 100 characters.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.Source);
        _ = ScientificSourcePolicy.RequireSourceType(request.Source.SourceType);
        var normalized = await normalizer.NormalizeAsync(request.Source, cancellationToken);
        return await persistence.CreateOrGetAsync(request with { IdempotencyKey = request.IdempotencyKey.Trim(), WorkflowType = request.WorkflowType.Trim() }, normalized, cancellationToken);
    }
}
