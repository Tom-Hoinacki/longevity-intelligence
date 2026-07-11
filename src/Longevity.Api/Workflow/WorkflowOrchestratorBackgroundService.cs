using Longevity.Application.Contracts;
using Longevity.Application.Orchestration;
using Microsoft.Extensions.Options;

namespace Longevity.Api.Workflow;

public sealed class WorkflowOrchestratorBackgroundService(
    IOptions<WorkflowOrchestratorOptions> options,
    ILogger<WorkflowOrchestratorBackgroundService> logger,
    IWorkflowRunProcessor? processor = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;

        if (!configuration.Enabled)
        {
            logger.LogInformation("Workflow orchestrator is disabled by configuration.");
            await WaitForCancellationAsync(stoppingToken);
            logger.LogInformation("Workflow orchestrator cancellation completed.");
            return;
        }

        if (processor is null)
        {
            logger.LogCritical("Workflow orchestrator is enabled but no {ProcessorType} is registered.", nameof(IWorkflowRunProcessor));
            throw new InvalidOperationException("Workflow orchestrator requires a registered IWorkflowRunProcessor when enabled.");
        }

        logger.LogInformation("Workflow orchestrator started with a polling interval of {PollingIntervalSeconds} seconds.", configuration.PollingIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await processor.ProcessNextAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Workflow processor iteration failed; no workflow state was assumed or concealed by the host.");
                }

                await Task.Delay(TimeSpan.FromSeconds(configuration.PollingIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cancellation is logged below so shutdown has a single clear signal.
        }
        finally
        {
            logger.LogInformation("Workflow orchestrator cancellation completed.");
        }
    }

    private static async Task WaitForCancellationAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
