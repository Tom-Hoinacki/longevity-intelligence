using System.Text.Json;
using Longevity.Application.Contracts;

namespace Longevity.Application.Validation;

public sealed class DeterministicClaimCandidateValidator : IClaimCandidateValidator
{
    public Task<DeterministicValidationResult> ValidateAsync(ClaimCandidateForValidation candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (candidate.ClaimText.Length > 10_000) errors.Add("claim_text_too_long");
        if (!StructuredClaimCandidateParser.TryParse(candidate.StructuredCandidateJson, out _, out var parserErrors)) errors.AddRange(parserErrors);
        var result = JsonSerializer.Serialize(new { schemaVersion = StructuredClaimCandidateParser.SchemaVersion, passed = errors.Count == 0, errors });
        return Task.FromResult(new DeterministicValidationResult(errors.Count == 0, result));
    }
}
