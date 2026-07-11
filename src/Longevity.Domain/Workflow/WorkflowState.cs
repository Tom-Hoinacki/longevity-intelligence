namespace Longevity.Domain.Workflow;

public sealed record WorkflowState
{
    private WorkflowState(string databaseValue) => DatabaseValue = databaseValue;

    public string DatabaseValue { get; }

    public override string ToString() => DatabaseValue;

    public static WorkflowState FromDatabaseValue(string databaseValue) => AllByDatabaseValue.TryGetValue(databaseValue, out var state)
        ? state
        : throw new ArgumentOutOfRangeException(nameof(databaseValue), databaseValue, "Unknown workflow state.");

    public static readonly WorkflowState Received = new("received");
    public static readonly WorkflowState SourceNormalized = new("source_normalized");
    public static readonly WorkflowState Extracting = new("extracting");
    public static readonly WorkflowState CandidateExtracted = new("candidate_extracted");
    public static readonly WorkflowState Validating = new("validating");
    public static readonly WorkflowState AwaitingHumanApproval = new("awaiting_human_approval");
    public static readonly WorkflowState Approved = new("approved");
    public static readonly WorkflowState Publishing = new("publishing");
    public static readonly WorkflowState Published = new("published");
    public static readonly WorkflowState NoCandidateExtracted = new("no_candidate_extracted");
    public static readonly WorkflowState ValidationFailed = new("validation_failed");
    public static readonly WorkflowState Rejected = new("rejected");
    public static readonly WorkflowState PublicationFailed = new("publication_failed");

    public static IReadOnlyList<WorkflowState> All { get; } = Array.AsReadOnly(
    [
        Received,
        SourceNormalized,
        Extracting,
        CandidateExtracted,
        Validating,
        AwaitingHumanApproval,
        Approved,
        Publishing,
        Published,
        NoCandidateExtracted,
        ValidationFailed,
        Rejected,
        PublicationFailed
    ]);

    public static IReadOnlySet<WorkflowState> WorkerRunnable { get; } = new HashSet<WorkflowState>
    {
        Received,
        SourceNormalized,
        Extracting,
        CandidateExtracted,
        Validating,
        Approved,
        Publishing
    };

    public static IReadOnlySet<WorkflowState> HumanWaiting { get; } = new HashSet<WorkflowState>
    {
        AwaitingHumanApproval
    };

    public static IReadOnlySet<WorkflowState> SuccessfulTerminal { get; } = new HashSet<WorkflowState>
    {
        Published
    };

    public static IReadOnlySet<WorkflowState> UnsuccessfulOrManuallyRecoverableTerminal { get; } = new HashSet<WorkflowState>
    {
        NoCandidateExtracted,
        ValidationFailed,
        Rejected,
        PublicationFailed
    };

    private static IReadOnlyDictionary<string, WorkflowState> AllByDatabaseValue { get; } = All.ToDictionary(state => state.DatabaseValue, StringComparer.Ordinal);
}
