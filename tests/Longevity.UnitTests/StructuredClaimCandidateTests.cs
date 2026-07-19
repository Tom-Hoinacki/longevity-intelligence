using Longevity.Application.Contracts;
using Longevity.Application.Validation;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class StructuredClaimCandidateTests
{
    [Fact]
    public void Parses_source_grounded_candidate_with_optional_fields()
    {
        const string json = """
            {"assetSlug":"nicotinamide-riboside","assetName":"Nicotinamide riboside","assetType":"compound","assetSummary":null,"claimType":"human trial","targetSystem":"cellular metabolism","population":"adults","outcomeMeasured":"NAD-related biomarker","evidenceLevel":"randomized_trial","evidenceDirection":"supports","effectSummary":"biomarker change","limitations":"short follow-up","relevanceScore":4.0,"evidenceScore":3.0,"hypeScore":2.0,"riskScore":1.0,"plainEnglishVerdict":"Evidence is limited."}
            """;

        Assert.True(StructuredClaimCandidateParser.TryParse(json, out var candidate, out var errors));
        Assert.Empty(errors);
        Assert.Equal("nicotinamide-riboside", candidate!.AssetSlug);
    }

    [Fact]
    public async Task Deterministic_validator_rejects_invalid_shape()
    {
        var candidate = new ClaimCandidateForValidation(new ClaimCandidateId(Guid.NewGuid()), new WorkflowRunId(Guid.NewGuid()), new SourceRecordId(Guid.NewGuid()), 1, 1, "A claim", "{}");
        var result = await new DeterministicClaimCandidateValidator().ValidateAsync(candidate, CancellationToken.None);
        Assert.False(result.Passed);
        Assert.Contains("assetSlug_required", result.ValidationResultJson);
    }
}
