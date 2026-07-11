using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class WorkflowStateTests
{
    [Fact]
    public void All_states_match_the_deployed_database_values()
    {
        var expected = new[]
        {
            "received", "source_normalized", "extracting", "candidate_extracted", "validating",
            "awaiting_human_approval", "approved", "publishing", "published", "no_candidate_extracted",
            "validation_failed", "rejected", "publication_failed"
        };

        Assert.Equal(expected, WorkflowState.All.Select(state => state.DatabaseValue));
    }

    [Fact]
    public void Worker_runnable_states_exclude_human_approval_waiting()
    {
        Assert.DoesNotContain(WorkflowState.AwaitingHumanApproval, WorkflowState.WorkerRunnable);
    }

    [Fact]
    public void Published_is_a_successful_terminal_state()
    {
        Assert.Contains(WorkflowState.Published, WorkflowState.SuccessfulTerminal);
    }

    [Fact]
    public void Rejected_and_validation_failed_are_not_automatically_runnable()
    {
        Assert.DoesNotContain(WorkflowState.Rejected, WorkflowState.WorkerRunnable);
        Assert.DoesNotContain(WorkflowState.ValidationFailed, WorkflowState.WorkerRunnable);
    }

    [Fact]
    public void Approval_decisions_use_lowercase_database_values()
    {
        Assert.Equal("approved", ApprovalDecision.Approved.DatabaseValue);
        Assert.Equal("rejected", ApprovalDecision.Rejected.DatabaseValue);
        Assert.Equal("revision_requested", ApprovalDecision.RevisionRequested.DatabaseValue);
    }
}
