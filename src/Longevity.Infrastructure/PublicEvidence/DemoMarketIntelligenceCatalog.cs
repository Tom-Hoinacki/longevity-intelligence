using Longevity.Application.PublicEvidence;

namespace Longevity.Infrastructure.PublicEvidence;

public sealed class DemoMarketIntelligenceCatalog : IMarketIntelligenceCatalog
{
    private static readonly Guid AssetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly MarketProviderSummary ProviderA = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), "example-longevity-labs", "Example Longevity Labs", ProviderType.Laboratory, null, "US-demo", true);
    private static readonly MarketProviderSummary ProviderB = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), "demo-health-market", "Demo Health Market", ProviderType.Retailer, null, "EU-demo", true);
    private static readonly Guid OfferingAId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid OfferingBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
    private static readonly MarketOfferingSummary OfferingA = new(OfferingAId, AssetId, "demo-asset", ProviderA, "illustrative-cellular-support-subscription", "Illustrative Cellular Support Subscription", OfferingType.Subscription, "30", "demo servings", "monthly", true, null);
    private static readonly MarketOfferingSummary OfferingB = new(OfferingBId, AssetId, "demo-asset", ProviderB, "illustrative-cellular-support-single-pack", "Illustrative Cellular Support Single Pack", OfferingType.PhysicalProduct, "1", "demo pack", null, true, null);
    private static readonly IReadOnlyList<PriceObservation> Prices =
    [
        new(Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc3"), OfferingAId, 31.00m, "USD", PricingBasis.RecurringSubscription, "month", "30", "demo servings", "US-demo", new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), "https://example.invalid/illustrative-price", "Illustrative demo source - not a real vendor", new DateTimeOffset(2026, 7, 1, 0, 1, 0, TimeSpan.Zero)),
        new(Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2"), OfferingAId, 29.00m, "USD", PricingBasis.RecurringSubscription, "month", "30", "demo servings", "US-demo", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), "https://example.invalid/illustrative-price", "Illustrative demo source - not a real vendor", new DateTimeOffset(2026, 6, 1, 0, 1, 0, TimeSpan.Zero)),
        new(Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1"), OfferingBId, 27.00m, "EUR", PricingBasis.OneTimePurchase, null, "1", "demo pack", "EU-demo", new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), "https://example.invalid/illustrative-eu-price", "Illustrative demo source - not a real vendor", new DateTimeOffset(2026, 6, 15, 0, 1, 0, TimeSpan.Zero))
    ];
    private static readonly IReadOnlyList<AvailabilityObservation> Availability =
    [
        new(Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd2"), OfferingAId, AvailabilityStatus.Waitlist, "US-demo", new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero), "https://example.invalid/illustrative-availability", "Illustrative demo source - not a real vendor", new DateTimeOffset(2026, 7, 2, 0, 1, 0, TimeSpan.Zero)),
        new(Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1"), OfferingAId, AvailabilityStatus.Available, "US-demo", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), "https://example.invalid/illustrative-availability", "Illustrative demo source - not a real vendor", new DateTimeOffset(2026, 6, 1, 0, 1, 0, TimeSpan.Zero))
    ];

    public Task<IReadOnlyList<MarketOfferingSummary>?> ListOfferingsForAssetAsync(string assetSlug, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MarketOfferingSummary>?>(assetSlug == "demo-asset" ? [WithSummary(OfferingA), WithSummary(OfferingB)] : null);
    }
    public Task<MarketOfferingDetail?> GetOfferingAsync(Guid offeringId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var offering = offeringId == OfferingAId ? OfferingA : offeringId == OfferingBId ? OfferingB : null;
        return Task.FromResult<MarketOfferingDetail?>(offering is null ? null : new(WithSummary(offering), null, null, "Latest recorded observations are historical demo data and may be stale; this is not medical advice."));
    }
    public Task<MarketHistoryPage<PriceObservation>?> ListPriceHistoryAsync(Guid offeringId, MarketHistoryQuery query, CancellationToken cancellationToken) => PageAsync(Prices.Where(p => p.OfferingId == offeringId), offeringId, query, cancellationToken);
    public Task<MarketHistoryPage<AvailabilityObservation>?> ListAvailabilityHistoryAsync(Guid offeringId, MarketHistoryQuery query, CancellationToken cancellationToken) => PageAsync(Availability.Where(a => a.OfferingId == offeringId), offeringId, query, cancellationToken);
    private static MarketOfferingSummary WithSummary(MarketOfferingSummary offering) => offering with { LatestRecordedObservation = new(Prices.Where(p => p.OfferingId == offering.Id).OrderByDescending(p => p.ObservedAt).ThenByDescending(p => p.Id).FirstOrDefault(), Availability.Where(a => a.OfferingId == offering.Id).OrderByDescending(a => a.ObservedAt).ThenByDescending(a => a.Id).FirstOrDefault(), "Latest recorded observation only; not a live price or availability guarantee.") };
    private static Task<MarketHistoryPage<T>?> PageAsync<T>(IEnumerable<T> source, Guid offeringId, MarketHistoryQuery query, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested(); if (offeringId != OfferingAId && offeringId != OfferingBId) return Task.FromResult<MarketHistoryPage<T>?>(null);
        var ordered = source.Where(x => GetObservedAt(x) >= (query.StartDate ?? DateTimeOffset.MinValue) && GetObservedAt(x) <= (query.EndDate ?? DateTimeOffset.MaxValue)).OrderByDescending(GetObservedAt).ThenByDescending(GetId).ToList();
        if (!string.IsNullOrWhiteSpace(query.Cursor)) { var (time, id) = MarketIntelligenceValidation.ParseCursor(query.Cursor); ordered = ordered.Where(x => GetObservedAt(x) < time || (GetObservedAt(x) == time && GetId(x).CompareTo(id) < 0)).ToList(); }
        var page = ordered.Take(query.Limit + 1).ToList(); var hasNext = page.Count > query.Limit; if (hasNext) page.RemoveAt(page.Count - 1);
        return Task.FromResult<MarketHistoryPage<T>?>(new(page, hasNext ? MarketIntelligenceValidation.Cursor(GetObservedAt(page[^1]), GetId(page[^1])) : null, query.Limit, hasNext));
    }
    private static DateTimeOffset GetObservedAt<T>(T item) => item switch { PriceObservation p => p.ObservedAt, AvailabilityObservation a => a.ObservedAt, _ => throw new InvalidOperationException() };
    private static Guid GetId<T>(T item) => item switch { PriceObservation p => p.Id, AvailabilityObservation a => a.Id, _ => throw new InvalidOperationException() };
}
