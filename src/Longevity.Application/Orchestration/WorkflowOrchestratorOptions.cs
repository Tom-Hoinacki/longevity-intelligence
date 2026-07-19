namespace Longevity.Application.Orchestration;

public sealed class WorkflowOrchestratorOptions
{
    public const string SectionName = "WorkflowOrchestrator";

    public bool Enabled { get; set; }

    public int PollingIntervalSeconds { get; set; } = 30;
    public int RetryDelaySeconds { get; set; } = 60;

    public void EnsureValid()
    {
        if (PollingIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PollingIntervalSeconds), "PollingIntervalSeconds must be greater than zero.");
        }
        if (RetryDelaySeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(RetryDelaySeconds), "RetryDelaySeconds must be greater than zero.");
    }

    public void EnsureValid(bool postgresEnabled)
    {
        EnsureValid();
        if (Enabled && !postgresEnabled)
            throw new ArgumentException("The workflow orchestrator requires PostgreSQL persistence when enabled.");
    }
}
