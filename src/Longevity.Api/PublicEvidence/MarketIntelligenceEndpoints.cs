using Longevity.Application.PublicEvidence;

namespace Longevity.Api.PublicEvidence;

public static class MarketIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapMarketIntelligenceApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").WithTags("Market intelligence");
        group.MapGet("/assets/{assetSlug}/offerings", async (string assetSlug, IMarketIntelligenceCatalog catalog, CancellationToken ct) => await Safe(async () => (await catalog.ListOfferingsForAssetAsync(assetSlug, ct)) is { } value ? Results.Ok(value) : Results.NotFound()));
        group.MapGet("/offerings/{offeringId:guid}", async (Guid offeringId, IMarketIntelligenceCatalog catalog, CancellationToken ct) => await Safe(async () => (await catalog.GetOfferingAsync(offeringId, ct)) is { } value ? Results.Ok(value) : Results.NotFound()));
        group.MapGet("/offerings/{offeringId:guid}/prices", async (Guid offeringId, string? cursor, int? limit, DateTimeOffset? startDate, DateTimeOffset? endDate, IMarketIntelligenceCatalog catalog, CancellationToken ct) => await History(() => catalog.ListPriceHistoryAsync(offeringId, MarketIntelligenceValidation.CreateQuery(cursor, limit, startDate, endDate), ct)));
        group.MapGet("/offerings/{offeringId:guid}/availability", async (Guid offeringId, string? cursor, int? limit, DateTimeOffset? startDate, DateTimeOffset? endDate, IMarketIntelligenceCatalog catalog, CancellationToken ct) => await History(() => catalog.ListAvailabilityHistoryAsync(offeringId, MarketIntelligenceValidation.CreateQuery(cursor, limit, startDate, endDate), ct)));
        return endpoints;
    }
    public static Task<IResult> MapForTestOfferings(string assetSlug, IMarketIntelligenceCatalog catalog) => Safe(async () => (await catalog.ListOfferingsForAssetAsync(assetSlug, CancellationToken.None)) is { } value ? Results.Ok(value) : Results.NotFound());
    public static Task<IResult> MapForTestDetail(Guid offeringId, IMarketIntelligenceCatalog catalog) => Safe(async () => (await catalog.GetOfferingAsync(offeringId, CancellationToken.None)) is { } value ? Results.Ok(value) : Results.NotFound());
    public static Task<IResult> MapForTestPrices(Guid offeringId, string? cursor, int? limit, IMarketIntelligenceCatalog catalog) => History(() => catalog.ListPriceHistoryAsync(offeringId, MarketIntelligenceValidation.CreateQuery(cursor, limit, null, null), CancellationToken.None));

    private static async Task<IResult> History<T>(Func<Task<MarketHistoryPage<T>?>> load) => await Safe(async () => await load() is { } value ? Results.Ok(value) : Results.NotFound());
    private static async Task<IResult> Safe(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (FormatException) { return Results.BadRequest(new { error = "invalid_cursor" }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (MarketIntelligenceUnavailableException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        catch (OperationCanceledException) { throw; }
        catch { return Results.StatusCode(StatusCodes.Status500InternalServerError); }
    }
}
