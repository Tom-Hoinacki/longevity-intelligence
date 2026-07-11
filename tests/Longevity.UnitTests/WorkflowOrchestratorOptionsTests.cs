using Longevity.Application.Orchestration;

namespace Longevity.UnitTests;

public sealed class WorkflowOrchestratorOptionsTests
{
    [Fact]
    public void Orchestrator_is_disabled_by_default()
    {
        var options = new WorkflowOrchestratorOptions();

        Assert.False(options.Enabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_polling_intervals_fail_validation(int pollingIntervalSeconds)
    {
        var options = new WorkflowOrchestratorOptions { PollingIntervalSeconds = pollingIntervalSeconds };

        Assert.Throws<ArgumentOutOfRangeException>(options.EnsureValid);
    }
}
