namespace Longevity.Api.Workflow;

public sealed class WorkflowIntakeApiOptions
{
    public const string SectionName = "WorkflowIntake";
    public const int MinimumAccessSecretLength = 32;
    public bool Enabled { get; init; }
    public string? AccessSecret { get; init; }

    public void EnsureValid(bool postgresEnabled)
    {
        if (!Enabled) return;
        if (!postgresEnabled) throw new ArgumentException("Workflow intake requires PostgreSQL persistence when enabled.");
        if (string.IsNullOrWhiteSpace(AccessSecret) || AccessSecret.Length < MinimumAccessSecretLength) throw new ArgumentException("Workflow intake requires a trusted access secret of at least 32 characters.");
    }
}
