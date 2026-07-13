using Longevity.Api.DependencyInjection;
using Longevity.Api.PublicEvidence;
using Longevity.Application.PublicEvidence;
using Longevity.Infrastructure.PublicEvidence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Longevity.Infrastructure.Tests.PublicEvidence;

public sealed class MarketIntelligenceTests
{
    private static readonly Guid OfferingA = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");

    [Fact]
    public void Validation_rejects_invalid_market_values()
    {
        Assert.Throws<ArgumentException>(() => MarketIntelligenceValidation.ValidateCurrency("usd"));
        Assert.Throws<ArgumentException>(() => MarketIntelligenceValidation.ValidatePositive(0, "amount"));
        Assert.Throws<ArgumentException>(() => MarketIntelligenceValidation.ValidatePositive(-1, "amount"));
        Assert.Throws<ArgumentException>(() => MarketIntelligenceValidation.ValidateUrl("ftp://bad.example"));
        Assert.Throws<ArgumentException>(() => MarketIntelligenceValidation.ValidateUrl("https://example.invalid/" + new string('a', 2050)));
        Assert.Throws<ArgumentException>(() => MarketIntelligenceValidation.ValidateObservedAt(DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketIntelligenceValidation.ValidateEnum((PricingBasis)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketIntelligenceValidation.ValidateEnum((AvailabilityStatus)999));
        Assert.Throws<ArgumentException>(() => MarketIntelligenceValidation.CreateQuery(null, 101, null, null));
        Assert.Throws<ArgumentException>(() => MarketIntelligenceValidation.CreateQuery(null, 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task Demo_mode_is_explicit_and_models_multiple_offerings_append_only_history_and_cursor_ordering()
    {
        var catalog = new DemoMarketIntelligenceCatalog();
        var offerings = await catalog.ListOfferingsForAssetAsync("demo-asset", CancellationToken.None);
        Assert.NotNull(offerings);
        Assert.Equal(2, offerings.Count);
        Assert.All(offerings, offering => Assert.Equal("demo-asset", offering.AssetSlug));
        Assert.NotEqual(offerings[0].Provider.Id, offerings[1].Provider.Id);

        var firstPage = await catalog.ListPriceHistoryAsync(OfferingA, MarketIntelligenceValidation.CreateQuery(null, 1, null, null), CancellationToken.None);
        Assert.NotNull(firstPage);
        Assert.True(firstPage.HasNextPage);
        Assert.Single(firstPage.Items);
        Assert.NotNull(firstPage.NextCursor);
        var secondPage = await catalog.ListPriceHistoryAsync(OfferingA, MarketIntelligenceValidation.CreateQuery(firstPage.NextCursor, 1, null, null), CancellationToken.None);
        Assert.NotNull(secondPage);
        Assert.Single(secondPage.Items);
        Assert.NotEqual(firstPage.Items[0].Id, secondPage.Items[0].Id);
        Assert.True(firstPage.Items[0].ObservedAt > secondPage.Items[0].ObservedAt);

        var availability = await catalog.ListAvailabilityHistoryAsync(OfferingA, MarketIntelligenceValidation.CreateQuery(null, 10, null, null), CancellationToken.None);
        Assert.NotNull(availability);
        Assert.Equal([AvailabilityStatus.Waitlist, AvailabilityStatus.Available], availability.Items.Select(item => item.Status).ToArray());
        Assert.Null(await catalog.GetOfferingAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await catalog.ListOfferingsForAssetAsync("missing-asset", CancellationToken.None));
    }

    [Fact]
    public void Provider_configuration_blocks_silent_demo_fallback_when_postgres_is_requested_without_postgres()
    {
        var options = new MarketIntelligenceOptions { Provider = MarketIntelligenceProvider.Postgres };
        Assert.Throws<ArgumentException>(() => options.EnsureValid(postgresEnabled: false));
        new MarketIntelligenceOptions { Provider = MarketIntelligenceProvider.Demo }.EnsureValid(postgresEnabled: false);
    }

    [Fact]
    public void Service_registration_uses_configured_market_provider()
    {
        var services = new ServiceCollection();
        services.AddMarketIntelligenceApi(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Postgres:Enabled"] = "false",
            ["MarketIntelligence:Provider"] = "Demo"
        }).Build());
        using var provider = services.BuildServiceProvider();
        Assert.IsType<DemoMarketIntelligenceCatalog>(provider.GetRequiredService<IMarketIntelligenceCatalog>());
    }

    [Fact]
    public async Task Api_returns_success_bad_request_not_found_and_service_unavailable_without_write_routes()
    {
        var ok = await MarketIntelligenceEndpoints.MapForTestOfferings("demo-asset", new DemoMarketIntelligenceCatalog());
        Assert.Equal(StatusCodes.Status200OK, Status(ok));
        var bad = await MarketIntelligenceEndpoints.MapForTestPrices(OfferingA, "not-base64", null, new DemoMarketIntelligenceCatalog());
        Assert.Equal(StatusCodes.Status400BadRequest, Status(bad));
        var missing = await MarketIntelligenceEndpoints.MapForTestDetail(Guid.NewGuid(), new DemoMarketIntelligenceCatalog());
        Assert.Equal(StatusCodes.Status404NotFound, Status(missing));
        var unavailable = await MarketIntelligenceEndpoints.MapForTestDetail(OfferingA, new UnavailableCatalog());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Status(unavailable));

        Assert.DoesNotContain(typeof(MarketOfferingSummary).Assembly.GetTypes(), type => type.Name.Contains("Affiliate", StringComparison.OrdinalIgnoreCase) || type.Name.Contains("Sponsorship", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contracts_do_not_reference_private_profiles_or_personal_health_data()
    {
        var contractNames = typeof(IMarketIntelligenceCatalog).Assembly.GetTypes().Where(type => type.Namespace == "Longevity.Application.PublicEvidence").Select(type => type.FullName ?? type.Name).ToArray();
        Assert.DoesNotContain(contractNames, name => name.Contains("PrivateProfile", StringComparison.OrdinalIgnoreCase) || name.Contains("PersonalHealth", StringComparison.OrdinalIgnoreCase));
    }

    private static int Status(IResult result) => (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? StatusCodes.Status200OK);

    private sealed class UnavailableCatalog : IMarketIntelligenceCatalog
    {
        public Task<IReadOnlyList<MarketOfferingSummary>?> ListOfferingsForAssetAsync(string assetSlug, CancellationToken cancellationToken) => throw new MarketIntelligenceUnavailableException("unavailable");
        public Task<MarketOfferingDetail?> GetOfferingAsync(Guid offeringId, CancellationToken cancellationToken) => throw new MarketIntelligenceUnavailableException("unavailable");
        public Task<MarketHistoryPage<PriceObservation>?> ListPriceHistoryAsync(Guid offeringId, MarketHistoryQuery query, CancellationToken cancellationToken) => throw new MarketIntelligenceUnavailableException("unavailable");
        public Task<MarketHistoryPage<AvailabilityObservation>?> ListAvailabilityHistoryAsync(Guid offeringId, MarketHistoryQuery query, CancellationToken cancellationToken) => throw new MarketIntelligenceUnavailableException("unavailable");
    }
}
