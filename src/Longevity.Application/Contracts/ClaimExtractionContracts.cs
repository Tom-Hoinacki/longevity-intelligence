namespace Longevity.Application.Contracts;

public sealed record ExtractedClaimCandidate(string ClaimText, string StructuredCandidateJson);

public sealed record ClaimExtractionResult(IReadOnlyList<ExtractedClaimCandidate> Candidates);
