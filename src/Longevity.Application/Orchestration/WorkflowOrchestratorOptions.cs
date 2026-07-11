namespace Longevity.Application.Orchestration;

public sealed class WorkflowOrchestratorOptions
{
    public const string SectionName = "WorkflowOrchestrator";

    public bool Enabled { get; set; }

    public int PollingIntervalSeconds { get; set; } = 30;

    public void EnsureValid()
    {
        if (PollingIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PollingIntervalSeconds), "PollingIntervalSeconds must be greater than zero.");
        }
    }
}
