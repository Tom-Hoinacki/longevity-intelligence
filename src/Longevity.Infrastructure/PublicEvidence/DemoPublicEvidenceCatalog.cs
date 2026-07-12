using Longevity.Application.PublicEvidence;
namespace Longevity.Infrastructure.PublicEvidence;
public sealed class DemoPublicEvidenceCatalog : IPublicEvidenceCatalog
{
    private static readonly Guid AssetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClaimId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly PublicEvidenceSource Source = new(SourceId, "illustrative", "Illustrative demo source", null, null, null, null, null, null, null);
    private static readonly PublicEvidenceClaim Claim = new(ClaimId, AssetId, "Illustrative demo claim for interface development.", "demo", null, null, null, null, null, 1, [new(Guid.Parse("44444444-4444-4444-4444-444444444444"), SourceId, "unknown", "illustrative", null, null, null, "Demo data is not evidence.", null, Source)]);
    private static readonly PublicEvidenceAsset Asset = new(AssetId, "demo-asset", "Demo longevity asset", "illustrative", "Deterministic placeholder data for local frontend development.", 1, 1);
    public Task<PublicEvidencePage<PublicEvidenceAsset>> ListAssetsAsync(int page, int pageSize, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new PublicEvidencePage<PublicEvidenceAsset>(page == 1 ? [Asset] : [], page, pageSize, false)); }
    public Task<PublicEvidenceAssetDetail?> GetAssetAsync(string slug, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<PublicEvidenceAssetDetail?>(slug == Asset.Slug ? new(Asset, [Claim]) : null); }
    public Task<IReadOnlyList<PublicEvidenceClaim>?> ListClaimsAsync(string slug, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<PublicEvidenceClaim>?>(slug == Asset.Slug ? [Claim] : null); }
    public Task<PublicEvidenceClaim?> GetClaimAsync(Guid claimId, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<PublicEvidenceClaim?>(claimId == Claim.Id ? Claim : null); }
}
