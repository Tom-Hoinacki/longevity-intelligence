namespace Longevity.Application.Contracts;

public interface IWorkflowRunProcessor
{
    Task ProcessNextAsync(CancellationToken cancellationToken);
}
