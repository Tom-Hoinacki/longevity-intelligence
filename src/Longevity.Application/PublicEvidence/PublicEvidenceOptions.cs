namespace Longevity.Application.PublicEvidence;
public enum PublicEvidenceProvider { Demo, Postgres }
public sealed class PublicEvidenceOptions
{
    public const string SectionName = "PublicEvidence";
    public PublicEvidenceProvider Provider { get; init; } = PublicEvidenceProvider.Demo;
    public void EnsureValid(bool postgresEnabled)
    {
        if (!Enum.IsDefined(Provider)) throw new ArgumentOutOfRangeException(nameof(Provider));
        if (Provider == PublicEvidenceProvider.Postgres && !postgresEnabled) throw new ArgumentException("Postgres public evidence provider requires Postgres to be enabled.");
    }
}
