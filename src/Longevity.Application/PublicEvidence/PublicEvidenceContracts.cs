namespace Longevity.Application.PublicEvidence;

public sealed record PublicEvidenceAsset(Guid Id, string Slug, string Name, string AssetType, string? ShortSummary, int ClaimCount, int SourceCount);
public sealed record PublicEvidenceSource(Guid Id, string SourceType, string Title, string? Url, string? PublicationName, DateOnly? PublishedDate, string? Doi, string? Pmid, string? TrialId, decimal? QualityScore);
public sealed record PublicEvidenceItem(Guid Id, Guid SourceId, string EvidenceDirection, string EvidenceLevel, string? Population, string? OutcomeMeasured, string? EffectSummary, string? Limitations, decimal? RelevanceScore, PublicEvidenceSource Source);
public sealed record PublicEvidenceClaim(Guid Id, Guid AssetId, string ClaimText, string? ClaimType, string? TargetSystem, decimal? EvidenceScore, decimal? HypeScore, decimal? RiskScore, string? PlainEnglishVerdict, int EvidenceCount, IReadOnlyList<PublicEvidenceItem> Evidence);
public sealed record PublicEvidenceAssetDetail(PublicEvidenceAsset Asset, IReadOnlyList<PublicEvidenceClaim> Claims);
public sealed record PublicEvidencePage<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasNextPage);
public interface IPublicEvidenceCatalog
{
    Task<PublicEvidencePage<PublicEvidenceAsset>> ListAssetsAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<PublicEvidenceAssetDetail?> GetAssetAsync(string slug, CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicEvidenceClaim>?> ListClaimsAsync(string slug, CancellationToken cancellationToken);
    Task<PublicEvidenceClaim?> GetClaimAsync(Guid claimId, CancellationToken cancellationToken);
}
