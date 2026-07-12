using Longevity.Application.PublicEvidence;
using Longevity.Infrastructure.PublicEvidence;
namespace Longevity.Infrastructure.Tests.PublicEvidence;
public sealed class PublicEvidenceTests
{
    [Fact]
    public async Task Demo_catalog_is_deterministic_and_supports_reads()
    {
        var catalog = new DemoPublicEvidenceCatalog(); var page = await catalog.ListAssetsAsync(1, 20, CancellationToken.None);
        Assert.Single(page.Items); Assert.Equal("demo-asset", page.Items[0].Slug);
        var detail = await catalog.GetAssetAsync("demo-asset", CancellationToken.None); Assert.NotNull(detail); Assert.Single(detail.Claims);
        Assert.NotNull(await catalog.GetClaimAsync(detail.Claims[0].Id, CancellationToken.None)); Assert.Null(await catalog.GetAssetAsync("missing", CancellationToken.None));
    }
    [Theory]
    [InlineData(PublicEvidenceProvider.Demo, true)]
    [InlineData(PublicEvidenceProvider.Postgres, false)]
    public void Provider_validation_enforces_postgres_boundary(PublicEvidenceProvider provider, bool valid)
    { var options = new PublicEvidenceOptions { Provider = provider }; if (valid) options.EnsureValid(false); else Assert.Throws<ArgumentException>(() => options.EnsureValid(false)); }
}
