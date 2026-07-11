namespace Longevity.Domain.Workflow;

public sealed record DeterministicValidationStatus
{
    private DeterministicValidationStatus(string databaseValue) => DatabaseValue = databaseValue;

    public string DatabaseValue { get; }

    public override string ToString() => DatabaseValue;

    public static readonly DeterministicValidationStatus Pending = new("pending");
    public static readonly DeterministicValidationStatus Passed = new("passed");
    public static readonly DeterministicValidationStatus Failed = new("failed");

    public static IReadOnlyList<DeterministicValidationStatus> All { get; } = Array.AsReadOnly([Pending, Passed, Failed]);
}
