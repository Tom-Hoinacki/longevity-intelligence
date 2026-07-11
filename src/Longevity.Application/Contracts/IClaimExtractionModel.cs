namespace Longevity.Application.Contracts;

public interface IClaimExtractionModel
{
    Task<ClaimExtractionResult> ExtractAsync(NormalizedScientificSource source, CancellationToken cancellationToken);
}
