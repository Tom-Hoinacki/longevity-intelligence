namespace Longevity.Application.Contracts;

public interface ISourceNormalizer
{
    Task<NormalizedScientificSource> NormalizeAsync(SubmittedAuthoritativeSource source, CancellationToken cancellationToken);
}
