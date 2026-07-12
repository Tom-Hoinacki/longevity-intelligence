namespace Longevity.Api.HumanReview;

public sealed class HumanReviewApiOptions
{
    public const string SectionName = "HumanReview";
    public const int MinimumAccessSecretLength = 32;

    public bool Enabled { get; init; }
    public string? AccessSecret { get; init; }

    public void EnsureValid(bool postgresEnabled)
    {
        if (!Enabled) return;
        if (!postgresEnabled)
            throw new ArgumentException("Human review requires PostgreSQL persistence when enabled.");
        if (string.IsNullOrWhiteSpace(AccessSecret) || AccessSecret.Length < MinimumAccessSecretLength)
            throw new ArgumentException("Human review requires a trusted access secret of at least 32 characters when enabled.");
    }
}
