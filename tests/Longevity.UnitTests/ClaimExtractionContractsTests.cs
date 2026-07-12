using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class ClaimExtractionContractsTests
{
    [Fact]
    public void Valid_metadata_and_candidate_are_accepted()
    {
        var metadata = Metadata();
        var candidate = Candidate("A claim", "{\"evidence\":true}");

        Assert.Equal("provider", metadata.ModelProvider);
        Assert.Equal("A claim", candidate.ClaimText);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_required_metadata_strings_are_rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => new ClaimExtractionExecutionMetadata(value, "provider", "model", "prompt"));
    }

    [Theory]
    [InlineData(-1, null, null, null)]
    [InlineData(null, -1, null, null)]
    [InlineData(null, null, -0.1, null)]
    [InlineData(null, null, null, -1)]
    public void Negative_execution_values_are_rejected(int? input, int? output, double? cost, int? latency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClaimExtractionExecutionMetadata(
            "schema", "provider", "model", "prompt", input, output,
            cost.HasValue ? (decimal?)Convert.ToDecimal(cost.Value) : null, latency));
    }

    [Fact]
    public void Invalid_or_non_object_candidate_json_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Candidate("claim", "not-json"));
        Assert.Throws<ArgumentException>(() => Candidate("claim", "[]"));
    }

    [Fact]
    public void Zero_candidates_are_valid_and_create_no_candidate_rows()
    {
        var claim = Claim();
        var request = new ClaimExtractionPersistenceRequest(claim, Source(claim.WorkflowRunId), new ClaimExtractionResult([], Metadata()));

        Assert.Empty(request.Candidates);
    }

    [Fact]
    public void Null_candidate_entries_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ClaimExtractionResult([null!], Metadata()));
    }

    [Fact]
    public void Candidates_preserve_output_order_use_one_based_ordinals_and_claimed_version()
    {
        var claim = Claim(version: 7);
        var request = new ClaimExtractionPersistenceRequest(
            claim,
            Source(claim.WorkflowRunId),
            new ClaimExtractionResult([Candidate("first", "{}"), Candidate("second", "{\"x\":1}")], Metadata()));

        Assert.Equal([1, 2], request.Candidates.Select(candidate => candidate.CandidateOrdinal));
        Assert.Equal(["first", "second"], request.Candidates.Select(candidate => candidate.Candidate.ClaimText));
        Assert.All(request.Candidates, candidate => Assert.Equal(7, candidate.CandidateVersion));
    }

    [Fact]
    public void Mismatched_workflow_and_source_identities_are_rejected_without_content_in_error()
    {
        var source = Source();
        var exception = Assert.Throws<ArgumentException>(() => new ClaimExtractionPersistenceRequest(
            Claim(workflowRunId: new WorkflowRunId(Guid.NewGuid())), source, new ClaimExtractionResult([], Metadata())));

        Assert.DoesNotContain(source.NormalizedText, exception.Message);
    }

    private static ClaimExtractionExecutionMetadata Metadata() =>
        new("schema-v1", "provider", "model", "prompt-v1", 1, 2, 0.01m, 3, "trace");

    private static ExtractedClaimCandidate Candidate(string claim, string json) => new(claim, json);

    private static ClaimedWorkflowRun Claim(int version = 3, WorkflowRunId? workflowRunId = null) =>
        new(workflowRunId ?? new WorkflowRunId(Guid.NewGuid()), WorkflowState.Extracting, version);

    private static NormalizedScientificSource Source(WorkflowRunId? workflowRunId = null) =>
        new(new SourceRecordId(Guid.NewGuid()), workflowRunId ?? new WorkflowRunId(Guid.NewGuid()), "doi:test", "title", "normalized source text");
}
