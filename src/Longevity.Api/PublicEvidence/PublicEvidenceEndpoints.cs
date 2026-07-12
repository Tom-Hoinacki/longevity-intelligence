using Longevity.Application.PublicEvidence;
namespace Longevity.Api.PublicEvidence;
public static class PublicEvidenceEndpoints
{
    public static IEndpointRouteBuilder MapPublicEvidenceApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").WithTags("Public evidence");
        group.MapGet("/assets", async (int? page, int? pageSize, IPublicEvidenceCatalog catalog, CancellationToken ct) => { var p = page ?? 1; var s = pageSize ?? 20; if (p < 1 || s is < 1 or > 100) return Results.BadRequest(new { error = "page must be positive and pageSize must be between 1 and 100." }); return Results.Ok(await catalog.ListAssetsAsync(p, s, ct)); });
        group.MapGet("/assets/{slug}", async (string slug, IPublicEvidenceCatalog catalog, CancellationToken ct) => (await catalog.GetAssetAsync(slug, ct)) is { } value ? Results.Ok(value) : Results.NotFound());
        group.MapGet("/assets/{slug}/claims", async (string slug, IPublicEvidenceCatalog catalog, CancellationToken ct) => (await catalog.ListClaimsAsync(slug, ct)) is { } value ? Results.Ok(value) : Results.NotFound());
        group.MapGet("/claims/{claimId:guid}", async (Guid claimId, IPublicEvidenceCatalog catalog, CancellationToken ct) => (await catalog.GetClaimAsync(claimId, ct)) is { } value ? Results.Ok(value) : Results.NotFound());
        return endpoints;
    }
}
