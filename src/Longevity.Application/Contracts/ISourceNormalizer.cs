using Longevity.Application.SourceNormalization;

namespace Longevity.Application.Contracts;

public interface ISourceNormalizer
{
    Task<ScientificSourceNormalizationResult> NormalizeAsync(SubmittedAuthoritativeSource source, CancellationToken cancellationToken);
}
