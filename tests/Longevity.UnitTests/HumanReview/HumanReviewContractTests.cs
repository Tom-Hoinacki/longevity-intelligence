using Longevity.Application.HumanReview;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests.HumanReview;

public sealed class DeterministicValidationSnapshotTests
{
    [Fact]
    public void Valid_object_json_is_trimmed_and_preserved()
    {
        var snapshot = new DeterministicValidationSnapshot(true, "  {\"valid\":true}  ");

        Assert.True(snapshot.Passed);
        Assert.Equal("{\"valid\":true}", snapshot.ValidationResultJson);
    }

    [Fact] public void Rejected_validation_is_rejected() => Assert.Throws<ArgumentException>(() => new DeterministicValidationSnapshot(false, "{}"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_json_is_rejected(string? json) => Assert.Throws<ArgumentException>(() => new DeterministicValidationSnapshot(true, json!));

    [Theory]
    [InlineData("not-json")]
    [InlineData("{")]
    public void Malformed_json_is_rejected_without_exposing_content(string json)
    {
        var exception = Assert.Throws<ArgumentException>(() => new DeterministicValidationSnapshot(true, json));
        Assert.DoesNotContain(json, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void Non_object_json_is_rejected_without_exposing_content(string json)
    {
        var exception = Assert.Throws<ArgumentException>(() => new DeterministicValidationSnapshot(true, json));
        Assert.DoesNotContain(json, exception.Message, StringComparison.Ordinal);
    }
}

public sealed class PendingHumanReviewCandidateTests
{
    [Fact]
    public void Valid_candidate_is_constructed_and_text_is_trimmed()
    {
        var candidate = Candidate(claimText: "  supported claim  ", json: "  {\"field\":1}  ");

        Assert.Equal("supported claim", candidate.ClaimText);
        Assert.Equal("{\"field\":1}", candidate.StructuredCandidateJson);
        Assert.True(candidate.Validation.Passed);
    }

    [Fact]
    public void Empty_identities_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Candidate(candidateId: new(Guid.Empty)));
        Assert.Throws<ArgumentException>(() => Candidate(runId: new(Guid.Empty)));
        Assert.Throws<ArgumentException>(() => Candidate(sourceId: new(Guid.Empty)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_version_is_rejected(int version) => Assert.Throws<ArgumentOutOfRangeException>(() => Candidate(version: version));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_ordinal_is_rejected(int ordinal) => Assert.Throws<ArgumentOutOfRangeException>(() => Candidate(ordinal: ordinal));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_claim_text_is_rejected(string text) => Assert.Throws<ArgumentException>(() => Candidate(claimText: text));

    [Theory]
    [InlineData("sensitive malformed json")]
    [InlineData("{")]
    public void Malformed_structured_json_is_rejected_without_exposure(string json)
    {
        var exception = Assert.Throws<ArgumentException>(() => Candidate(json: json));
        Assert.DoesNotContain(json, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("false")]
    public void Non_object_structured_json_is_rejected(string json) => Assert.Throws<ArgumentException>(() => Candidate(json: json));

    [Fact] public void Null_validation_is_rejected() => Assert.Throws<ArgumentNullException>(() => Candidate(useNullValidation: true));

    internal static PendingHumanReviewCandidate Candidate(
        ClaimCandidateId? candidateId = null, WorkflowRunId? runId = null, SourceRecordId? sourceId = null,
        int version = 1, int ordinal = 1, string claimText = "claim", string json = "{}",
        DeterministicValidationSnapshot? validation = default, bool useNullValidation = false) =>
        new(candidateId ?? new(Guid.NewGuid()), runId ?? new(Guid.NewGuid()), sourceId ?? new(Guid.NewGuid()), version, ordinal,
            claimText, json, useNullValidation ? null! : validation ?? new(true, "{}"));
}

public sealed class PendingHumanReviewBatchTests
{
    [Fact]
    public void Candidates_are_sorted_and_snapshotted_immutably()
    {
        var run = Run();
        var input = new List<PendingHumanReviewCandidate> { Candidate(run, 2), Candidate(run, 1) };
        var batch = Batch(run, input);
        input.Clear();

        Assert.Equal([1, 2], batch.Candidates.Select(candidate => candidate.CandidateOrdinal));
        Assert.Equal(2, batch.Candidates.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<PendingHumanReviewCandidate>)batch.Candidates).Clear());
    }

    [Fact] public void Empty_workflow_identity_is_rejected() => Assert.Throws<ArgumentException>(() => Batch(new(Guid.Empty), [Candidate(Run(), 1)]));
    [Theory] [InlineData(0)] [InlineData(-1)] public void Nonpositive_expected_version_is_rejected(int version) { var run = Run(); Assert.Throws<ArgumentOutOfRangeException>(() => Batch(run, [Candidate(run, 1)], version)); }
    [Fact] public void Wrong_state_is_rejected() { var run = Run(); Assert.Throws<ArgumentException>(() => Batch(run, [Candidate(run, 1)], state: WorkflowState.Received)); Assert.Throws<ArgumentException>(() => Batch(run, [Candidate(run, 1)], state: WorkflowState.Approved)); }
    [Fact] public void Null_or_empty_candidates_are_rejected() { var run = Run(); Assert.Throws<ArgumentException>(() => Batch(run, null!)); Assert.Throws<ArgumentException>(() => Batch(run, [])); }
    [Fact] public void Null_candidate_entries_are_rejected() { var run = Run(); Assert.Throws<ArgumentException>(() => Batch(run, [null!])); }
    [Fact] public void Mixed_workflow_identities_are_rejected() { var run = Run(); Assert.Throws<ArgumentException>(() => Batch(run, [Candidate(run, 1), Candidate(Run(), 2)])); }
    [Fact] public void Mixed_candidate_versions_are_rejected() { var run = Run(); Assert.Throws<ArgumentException>(() => Batch(run, [Candidate(run, 1), Candidate(run, 2, version: 2)])); }
    [Fact] public void Duplicate_candidate_identities_are_rejected() { var run = Run(); var id = new ClaimCandidateId(Guid.NewGuid()); Assert.Throws<ArgumentException>(() => Batch(run, [Candidate(run, 1, id), Candidate(run, 2, id)])); }
    [Fact] public void Duplicate_ordinals_are_rejected() { var run = Run(); Assert.Throws<ArgumentException>(() => Batch(run, [Candidate(run, 1), Candidate(run, 1)])); }
    [Fact] public void Noncontiguous_ordinals_are_rejected() { var run = Run(); Assert.Throws<ArgumentException>(() => Batch(run, [Candidate(run, 1), Candidate(run, 3)])); }
    [Fact] public void Candidate_that_did_not_pass_validation_is_rejected()
    {
        var run = Run();
        var candidate = Candidate(run, 1);
        var passedField = typeof(DeterministicValidationSnapshot).GetField("<Passed>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        passedField.SetValue(candidate.Validation, false);
        Assert.Throws<ArgumentException>(() => Batch(run, [candidate]));
    }

    private static WorkflowRunId Run() => new(Guid.NewGuid());
    private static PendingHumanReviewCandidate Candidate(WorkflowRunId run, int ordinal, ClaimCandidateId? id = null, int version = 1) =>
        new(id ?? new(Guid.NewGuid()), run, new(Guid.NewGuid()), version, ordinal, "claim", "{}", new(true, "{}"));
    private static PendingHumanReviewBatch Batch(WorkflowRunId run, IReadOnlyList<PendingHumanReviewCandidate> candidates, int version = 4, WorkflowState? state = null) => new(run, version, state ?? WorkflowState.AwaitingHumanApproval, candidates);
}
