namespace Longevity.Application.PublicEvidence;

public enum MarketIntelligenceProvider { Demo, Postgres }
public enum ProviderType { Manufacturer, Retailer, Clinic, Laboratory, Pharmacy, SubscriptionService, Marketplace, Other }
public enum OfferingType { PhysicalProduct, Subscription, ClinicalService, LaboratoryTest, Membership, Device, SoftwareService, Other }
public enum PricingBasis { OneTimePurchase, RecurringSubscription, PerTreatment, PerTest, PerVisit, PerUnit, Other }
public enum AvailabilityStatus { Available, OutOfStock, Waitlist, Unavailable, Discontinued, Unknown }

public sealed class MarketIntelligenceOptions
{
    public const string SectionName = "MarketIntelligence";
    public MarketIntelligenceProvider Provider { get; init; } = MarketIntelligenceProvider.Demo;
    public void EnsureValid(bool postgresEnabled)
    {
        if (!Enum.IsDefined(Provider)) throw new ArgumentOutOfRangeException(nameof(Provider));
        if (Provider == MarketIntelligenceProvider.Postgres && !postgresEnabled) throw new ArgumentException("Postgres market intelligence provider requires Postgres to be enabled.");
    }
}

public sealed record MarketProviderSummary(Guid Id, string Slug, string DisplayName, ProviderType ProviderType, string? CanonicalWebsiteUrl, string? PrimaryRegion, bool IsActive);
public sealed record MarketOfferingSummary(Guid Id, Guid AssetId, string AssetSlug, MarketProviderSummary Provider, string Slug, string DisplayName, OfferingType OfferingType, string? PackageQuantity, string? PackageUnit, string? BillingCadence, bool IsActive, MarketObservationSummary? LatestRecordedObservation);
public sealed record MarketOfferingDetail(MarketOfferingSummary Offering, string? ProviderProductCode, string? CanonicalUrl, string StalenessNotice);
public sealed record PriceObservation(Guid Id, Guid OfferingId, decimal Amount, string CurrencyCode, PricingBasis PricingBasis, string? BillingInterval, string? Quantity, string? QuantityUnit, string? GeographicMarket, DateTimeOffset ObservedAt, string SourceUrl, string? SourceLabel, DateTimeOffset CreatedAt);
public sealed record AvailabilityObservation(Guid Id, Guid OfferingId, AvailabilityStatus Status, string? GeographicMarket, DateTimeOffset ObservedAt, string SourceUrl, string? SourceLabel, DateTimeOffset CreatedAt);
public sealed record MarketObservationSummary(PriceObservation? LatestPrice, AvailabilityObservation? LatestAvailability, string Notice);
public sealed record MarketHistoryPage<T>(IReadOnlyList<T> Items, string? NextCursor, int Limit, bool HasNextPage);
public sealed record MarketHistoryQuery(string? Cursor, int Limit, DateTimeOffset? StartDate, DateTimeOffset? EndDate);

public interface IMarketIntelligenceCatalog
{
    Task<IReadOnlyList<MarketOfferingSummary>?> ListOfferingsForAssetAsync(string assetSlug, CancellationToken cancellationToken);
    Task<MarketOfferingDetail?> GetOfferingAsync(Guid offeringId, CancellationToken cancellationToken);
    Task<MarketHistoryPage<PriceObservation>?> ListPriceHistoryAsync(Guid offeringId, MarketHistoryQuery query, CancellationToken cancellationToken);
    Task<MarketHistoryPage<AvailabilityObservation>?> ListAvailabilityHistoryAsync(Guid offeringId, MarketHistoryQuery query, CancellationToken cancellationToken);
}

public sealed class MarketIntelligenceUnavailableException : Exception
{
    public MarketIntelligenceUnavailableException(string message, Exception? innerException = null) : base(message, innerException) { }
}

public static class MarketIntelligenceValidation
{
    public const int MaxLimit = 100;
    public const int DefaultLimit = 25;
    public static readonly TimeSpan FutureSkew = TimeSpan.FromDays(1);
    public static MarketHistoryQuery CreateQuery(string? cursor, int? limit, DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        var boundedLimit = limit ?? DefaultLimit;
        if (boundedLimit < 1 || boundedLimit > MaxLimit) throw new ArgumentException("limit must be between 1 and 100.");
        if (startDate.HasValue && endDate.HasValue && startDate > endDate) throw new ArgumentException("startDate must be before or equal to endDate.");
        if (!string.IsNullOrWhiteSpace(cursor)) ParseCursor(cursor);
        return new(cursor, boundedLimit, startDate, endDate);
    }
    public static (DateTimeOffset ObservedAt, Guid Id) ParseCursor(string cursor)
    {
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var parts = decoded.Split('|');
        if (parts.Length != 2 || !DateTimeOffset.TryParse(parts[0], out var observedAt) || !Guid.TryParse(parts[1], out var id)) throw new FormatException("Invalid cursor.");
        return (observedAt, id);
    }
    public static string Cursor(DateTimeOffset observedAt, Guid id) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{observedAt:O}|{id:D}"));
    public static void ValidateCurrency(string value) { if (value.Length != 3 || value.Any(c => c < 'A' || c > 'Z')) throw new ArgumentException("currencyCode must be three uppercase letters."); }
    public static void ValidatePositive(decimal amount, string name) { if (amount <= 0) throw new ArgumentException($"{name} must be positive."); }
    public static void ValidateUrl(string url) { if (url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) throw new ArgumentException("URL must be absolute HTTP(S) and at most 2048 characters."); }
    public static void ValidateObservedAt(DateTimeOffset observedAt, DateTimeOffset now) { if (observedAt > now.Add(FutureSkew)) throw new ArgumentException("observedAt is too far in the future."); }
    public static void ValidateEnum<T>(T value) where T : struct, Enum { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
}
