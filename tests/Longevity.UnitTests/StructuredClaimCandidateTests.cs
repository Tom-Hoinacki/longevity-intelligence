using Longevity.Application.Contracts;
using Longevity.Application.Validation;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class StructuredClaimCandidateTests
{
    private const string ValidJson = """
        {"assetSlug":"nicotinamide-riboside","assetName":"Nicotinamide riboside","assetType":"compound","assetSummary":null,"claimType":"human trial","targetSystem":"cellular metabolism","population":"adults","outcomeMeasured":"NAD-related biomarker","evidenceLevel":"randomized_controlled_trial","evidenceDirection":"supports","effectSummary":"biomarker change","limitations":"short follow-up","supportingExcerpt":"NAD-related biomarker increased","sampleSize":120,"replicationCount":1,"directness":"direct","riskOfBias":"moderate","consistency":"consistent","publicationStatus":"peer_reviewed","isRetracted":false,"hasSeriousMethodologicalLimitations":false,"conflictOfInterest":"none"}
        """;

    [Fact]
    public void Parses_source_grounded_candidate_with_optional_fields()
    {
        Assert.True(StructuredClaimCandidateParser.TryParse(ValidJson, out var candidate, out var errors));
        Assert.Empty(errors);
        Assert.Equal("nicotinamide-riboside", candidate!.AssetSlug);
    }

    [Fact]
    public async Task Deterministic_validator_rejects_invalid_shape()
    {
        var candidate = new ClaimCandidateForValidation(new ClaimCandidateId(Guid.NewGuid()), new WorkflowRunId(Guid.NewGuid()), new SourceRecordId(Guid.NewGuid()), 1, 1, "A claim", "{}", "source text");
        var result = await new DeterministicClaimCandidateValidator().ValidateAsync(candidate, CancellationToken.None);
        Assert.False(result.Passed);
        Assert.Contains("assetSlug_required", result.ValidationResultJson);
    }

    [Fact]
    public async Task Validator_requires_exact_source_provenance_and_produces_deterministic_policy_score()
    {
        var candidate = Candidate(ValidJson, "Prefix. NAD-related biomarker increased. Suffix.");
        var validator = new DeterministicClaimCandidateValidator();
        var first = await validator.ValidateAsync(candidate, default);
        var second = await validator.ValidateAsync(candidate, default);
        Assert.True(first.Passed);
        Assert.Equal(first.ValidationResultJson, second.ValidationResultJson);
        Assert.Contains("evidence-scoring-v1", first.ValidationResultJson);
        Assert.True(DeterministicValidationArtifactParser.TryReadScoring(first.ValidationResultJson, out var scoring));
        Assert.InRange(scoring!.PublicScore, 0m, 5m);
    }

    [Theory]
    [InlineData("evidenceLevel", "unsupported", "evidence_level_unsupported")]
    [InlineData("assetType", "unsupported", "asset_type_unsupported")]
    [InlineData("riskOfBias", "unknown", "risk_of_bias_unsupported")]
    public void Unsupported_enums_are_rejected(string property, string value, string expectedError)
    {
        var invalid = ValidJson.Replace($"\"{property}\":\"{PropertyValue(property)}\"", $"\"{property}\":\"{value}\"", StringComparison.Ordinal);
        Assert.False(StructuredClaimCandidateParser.TryParse(invalid, out _, out var errors));
        Assert.Contains(expectedError, errors);
    }

    [Fact]
    public async Task Missing_source_invalid_relationship_and_scoring_range_are_rejected()
    {
        var validator = new DeterministicClaimCandidateValidator();
        var missing = await validator.ValidateAsync(Candidate(ValidJson, string.Empty), default);
        var unrelated = await validator.ValidateAsync(Candidate(ValidJson, "different source"), default);
        var invalidScoreInput = await validator.ValidateAsync(Candidate(ValidJson.Replace("\"sampleSize\":120", "\"sampleSize\":-1", StringComparison.Ordinal), "NAD-related biomarker increased"), default);
        Assert.Contains("normalized_source_missing", missing.ValidationResultJson);
        Assert.Contains("supporting_excerpt_not_found", unrelated.ValidationResultJson);
        Assert.Contains("sampleSize_range", invalidScoreInput.ValidationResultJson);
    }

    [Fact]
    public async Task Oversized_claim_is_rejected()
    {
        var candidate = Candidate(ValidJson, "NAD-related biomarker increased") with { };
        var oversized = new ClaimCandidateForValidation(candidate.CandidateId, candidate.WorkflowRunId, candidate.SourceRecordId, 1, 1, new string('x', StructuredClaimCandidateParser.MaximumClaimTextLength + 1), ValidJson, candidate.NormalizedSourceText);
        var result = await new DeterministicClaimCandidateValidator().ValidateAsync(oversized, default);
        Assert.Contains("claim_text_too_long", result.ValidationResultJson);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"claim-candidate-v1\",\"passed\":true,\"scoring\":{\"policyId\":\"evidence-scoring-v1\",\"score\":60,\"publicScore\":3,\"verdict\":\"moderate\",\"alignment\":\"supports\",\"reasonCodes\":[\"reason\"]}}")]
    [InlineData("{\"schemaVersion\":\"claim-candidate-v2\",\"passed\":true,\"scoring\":{\"policyId\":\"evidence-scoring-v1\",\"score\":60,\"publicScore\":4,\"verdict\":\"moderate\",\"alignment\":\"supports\",\"reasonCodes\":[\"reason\"]}}")]
    [InlineData("{\"schemaVersion\":\"claim-candidate-v2\",\"passed\":true,\"scoring\":{\"policyId\":\"evidence-scoring-v1\",\"score\":60,\"publicScore\":3,\"verdict\":\"medical_fact\",\"alignment\":\"supports\",\"reasonCodes\":[\"reason\"]}}")]
    [InlineData("{\"schemaVersion\":\"claim-candidate-v2\",\"passed\":true,\"scoring\":{\"policyId\":\"evidence-scoring-v1\",\"score\":60,\"publicScore\":3,\"verdict\":\"moderate\",\"alignment\":\"supports\",\"reasonCodes\":[]}}")]
    public void Invalid_or_tampered_scoring_artifacts_are_rejected(string json) =>
        Assert.False(DeterministicValidationArtifactParser.TryReadScoring(json, out _));

    private static ClaimCandidateForValidation Candidate(string json, string source) => new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), 1, 1, "A claim", json, source);
    private static string PropertyValue(string property) => property switch { "evidenceLevel" => "randomized_controlled_trial", "assetType" => "compound", "riskOfBias" => "moderate", _ => throw new ArgumentOutOfRangeException(nameof(property)) };
}
