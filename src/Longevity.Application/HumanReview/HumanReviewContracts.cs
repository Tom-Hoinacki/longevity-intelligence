using System.Text.Json;
using Longevity.Domain.Workflow;

namespace Longevity.Application.HumanReview;

public enum HumanReviewDecision { Approve, Reject }

public sealed record PendingHumanReviewCandidate
{
    public PendingHumanReviewCandidate(ClaimCandidateId candidateId, WorkflowRunId workflowRunId, SourceRecordId sourceRecordId, int candidateVersion, int candidateOrdinal, string claimText, string structuredCandidateJson, DeterministicValidationSnapshot validation)
    {
        if (candidateVersion < 1 || candidateOrdinal < 1) throw new ArgumentOutOfRangeException(nameof(candidateVersion));
        if (candidateId.Value == Guid.Empty || workflowRunId.Value == Guid.Empty || sourceRecordId.Value == Guid.Empty) throw new ArgumentException("Review identities must be non-empty.");
        CandidateId = candidateId; WorkflowRunId = workflowRunId; SourceRecordId = sourceRecordId; CandidateVersion = candidateVersion; CandidateOrdinal = candidateOrdinal;
        ClaimText = Required(claimText, nameof(claimText)); StructuredCandidateJson = ObjectJson(structuredCandidateJson, nameof(structuredCandidateJson)); Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }
    public ClaimCandidateId CandidateId { get; }
    public WorkflowRunId WorkflowRunId { get; }
    public SourceRecordId SourceRecordId { get; }
    public int CandidateVersion { get; }
    public int CandidateOrdinal { get; }
    public string ClaimText { get; }
    public string StructuredCandidateJson { get; }
    public DeterministicValidationSnapshot Validation { get; }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A review value must be non-empty.", name) : value.Trim();
    private static string ObjectJson(string value, string name) { var text = Required(value, name); try { using var doc = JsonDocument.Parse(text); if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Review JSON must have an object root.", name); } catch (JsonException e) { throw new ArgumentException("Review JSON must be valid.", name, e); } return text; }
}

public sealed record DeterministicValidationSnapshot
{
    public DeterministicValidationSnapshot(bool passed, string resultJson) { if (!passed) throw new ArgumentException("Only validated candidates may enter review.", nameof(passed)); Passed = passed; ValidationResultJson = ObjectJson(resultJson); }
    public bool Passed { get; }
    public string ValidationResultJson { get; }
    private static string ObjectJson(string value) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Validation result JSON must be non-empty.", nameof(value)); try { using var doc = JsonDocument.Parse(value); if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Validation result JSON must have an object root.", nameof(value)); } catch (JsonException e) { throw new ArgumentException("Validation result JSON must be valid.", nameof(value), e); } return value.Trim(); }
}

public sealed record PendingHumanReviewBatch
{
    public PendingHumanReviewBatch(WorkflowRunId workflowRunId, int expectedWorkflowVersion, WorkflowState state, IReadOnlyList<PendingHumanReviewCandidate> candidates)
    {
        if (workflowRunId.Value == Guid.Empty) throw new ArgumentException("Workflow-run identity must be non-empty.", nameof(workflowRunId));
        if (expectedWorkflowVersion < 1) throw new ArgumentOutOfRangeException(nameof(expectedWorkflowVersion));
        if (state != WorkflowState.AwaitingHumanApproval) throw new ArgumentException("Review batch is not awaiting human approval.", nameof(state));
        if (candidates is null || candidates.Count == 0 || candidates.Any(c => c is null)) throw new ArgumentException("Review candidates must be non-empty.", nameof(candidates));
        var ordered = candidates.OrderBy(c => c.CandidateOrdinal).ToArray();
        if (ordered.Any(c => c.WorkflowRunId != workflowRunId) || ordered.Select(c => c.CandidateVersion).Distinct().Count() != 1 || ordered.Select(c => c.CandidateId).Distinct().Count() != ordered.Length || ordered.Select(c => c.CandidateOrdinal).Distinct().Count() != ordered.Length || ordered.Select(c => c.CandidateOrdinal).Select((v, i) => v == i + 1).Any(ok => !ok) || ordered.Any(c => !c.Validation.Passed)) throw new ArgumentException("Review candidates violate eligibility invariants.", nameof(candidates));
        WorkflowRunId = workflowRunId; ExpectedWorkflowVersion = expectedWorkflowVersion; State = state; Candidates = Array.AsReadOnly(ordered);
    }
    public WorkflowRunId WorkflowRunId { get; }
    public int ExpectedWorkflowVersion { get; }
    public WorkflowState State { get; }
    public IReadOnlyList<PendingHumanReviewCandidate> Candidates { get; }
}

public sealed record HumanReviewDecisionRequest(WorkflowRunId WorkflowRunId, HumanReviewDecision Decision, string ReviewerIdentity, string DecisionId, string? Reason = null, string? Note = null);
public sealed record HumanReviewPersistenceRequest(WorkflowRunId WorkflowRunId, int ExpectedWorkflowVersion, string DecisionId, HumanReviewDecision Decision, string ReviewerIdentity, string? Reason, string? Note, DateTimeOffset DecisionAt, WorkflowState TargetState);
public sealed record HumanReviewDecisionResult(WorkflowRunId WorkflowRunId, string DecisionId, HumanReviewDecision Decision, WorkflowState TargetState, DateTimeOffset DecisionAt);
public interface IHumanReviewPersistence { Task<PendingHumanReviewBatch?> LoadPendingAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken); Task<HumanReviewDecisionResult> AppendDecisionAsync(HumanReviewPersistenceRequest request, CancellationToken cancellationToken); }
public interface IHumanReviewService { Task<PendingHumanReviewBatch?> LoadAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken); Task<HumanReviewDecisionResult> DecideAsync(HumanReviewDecisionRequest request, CancellationToken cancellationToken); }
