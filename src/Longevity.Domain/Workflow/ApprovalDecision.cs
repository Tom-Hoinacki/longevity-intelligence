namespace Longevity.Domain.Workflow;

public sealed record ApprovalDecision
{
    private ApprovalDecision(string databaseValue) => DatabaseValue = databaseValue;

    public string DatabaseValue { get; }

    public override string ToString() => DatabaseValue;

    public static readonly ApprovalDecision Approved = new("approved");
    public static readonly ApprovalDecision Rejected = new("rejected");
    public static readonly ApprovalDecision RevisionRequested = new("revision_requested");

    public static IReadOnlyList<ApprovalDecision> All { get; } = Array.AsReadOnly([Approved, Rejected, RevisionRequested]);
}
