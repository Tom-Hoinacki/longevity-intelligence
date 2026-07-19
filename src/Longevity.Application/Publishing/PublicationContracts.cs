using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Longevity.Domain.Workflow;

namespace Longevity.Application.Publishing;

public sealed record PublicationSource
{
    public PublicationSource(SourceRecordId sourceRecordId, WorkflowRunId workflowRunId, string identityKey, string title, string? canonicalUrl, string sourceType = "unknown")
    {
        if (sourceRecordId.Value == Guid.Empty || workflowRunId.Value == Guid.Empty) throw new ArgumentException("Publication identities must be non-empty.");
        SourceRecordId = sourceRecordId; WorkflowRunId = workflowRunId;
        IdentityKey = Required(identityKey, nameof(identityKey)); Title = Required(title, nameof(title)); CanonicalUrl = string.IsNullOrWhiteSpace(canonicalUrl) ? null : canonicalUrl.Trim(); SourceType = Required(sourceType, nameof(sourceType));
    }
    public SourceRecordId SourceRecordId { get; }
    public WorkflowRunId WorkflowRunId { get; }
    public string IdentityKey { get; }
    public string Title { get; }
    public string? CanonicalUrl { get; }
    public string SourceType { get; }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A required publication value is missing.", name) : value.Trim();
}

public sealed record PublicationClaim
{
    public PublicationClaim(ClaimCandidateId candidateId, WorkflowRunId workflowRunId, SourceRecordId sourceRecordId, int ordinal, string claimText, string structuredCandidateJson, bool validationPassed, bool humanApproved, string deterministicValidationJson = "{}")
    {
        if (candidateId.Value == Guid.Empty || workflowRunId.Value == Guid.Empty || sourceRecordId.Value == Guid.Empty) throw new ArgumentException("Publication identities must be non-empty.");
        if (ordinal < 1) throw new ArgumentOutOfRangeException(nameof(ordinal));
        CandidateId = candidateId; WorkflowRunId = workflowRunId; SourceRecordId = sourceRecordId; Ordinal = ordinal;
        ClaimText = Required(claimText, nameof(claimText)); StructuredCandidateJson = ObjectJson(structuredCandidateJson); ValidationPassed = validationPassed; HumanApproved = humanApproved; DeterministicValidationJson = ObjectJson(deterministicValidationJson);
    }
    public ClaimCandidateId CandidateId { get; }
    public WorkflowRunId WorkflowRunId { get; }
    public SourceRecordId SourceRecordId { get; }
    public int Ordinal { get; }
    public string ClaimText { get; }
    public string StructuredCandidateJson { get; }
    public string DeterministicValidationJson { get; }
    public bool ValidationPassed { get; }
    public bool HumanApproved { get; }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A required publication value is missing.", name) : value.Trim();
    private static string ObjectJson(string value) { value = Required(value, nameof(value)); try { using var d = JsonDocument.Parse(value); if (d.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Structured publication JSON must be an object."); return value; } catch (JsonException e) { throw new ArgumentException("Structured publication JSON must be valid.", e); } }
}

public sealed record PublicationEvidenceLink
{
    public PublicationEvidenceLink(ClaimCandidateId candidateId, SourceRecordId sourceRecordId, string evidenceType)
    {
        if (candidateId.Value == Guid.Empty || sourceRecordId.Value == Guid.Empty) throw new ArgumentException("Evidence identities must be non-empty.");
        CandidateId = candidateId; SourceRecordId = sourceRecordId;
        EvidenceType = string.IsNullOrWhiteSpace(evidenceType) ? throw new ArgumentException("Evidence type is required.", nameof(evidenceType)) : evidenceType.Trim();
    }
    public ClaimCandidateId CandidateId { get; }
    public SourceRecordId SourceRecordId { get; }
    public string EvidenceType { get; }
}

public sealed record ApprovedPublicationBatch
{
    public ApprovedPublicationBatch(WorkflowRunId workflowRunId, int workflowRunVersion, WorkflowState workflowState, string approvalIdentity, DateTimeOffset approvedAt, string reviewerIdentity, PublicationSource source, IEnumerable<PublicationClaim> claims, IEnumerable<PublicationEvidenceLink> evidenceLinks)
    { WorkflowRunId = workflowRunId; WorkflowRunVersion = workflowRunVersion; WorkflowState = workflowState; ApprovalIdentity = Required(approvalIdentity); ApprovedAt = approvedAt; ReviewerIdentity = Required(reviewerIdentity); Source = source ?? throw new ArgumentNullException(nameof(source)); Claims = new ReadOnlyCollection<PublicationClaim>((claims ?? throw new ArgumentNullException(nameof(claims))).ToList()); EvidenceLinks = new ReadOnlyCollection<PublicationEvidenceLink>((evidenceLinks ?? throw new ArgumentNullException(nameof(evidenceLinks))).ToList()); }
    public WorkflowRunId WorkflowRunId { get; } public int WorkflowRunVersion { get; } public WorkflowState WorkflowState { get; } public string ApprovalIdentity { get; } public DateTimeOffset ApprovedAt { get; } public string ReviewerIdentity { get; } public PublicationSource Source { get; } public IReadOnlyList<PublicationClaim> Claims { get; } public IReadOnlyList<PublicationEvidenceLink> EvidenceLinks { get; }
    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Approval identity is required.") : value.Trim();
}

public sealed record AtomicPublicationCommand(string IdempotencyKey, string ContentFingerprint, ApprovedPublicationBatch Batch);
public enum AtomicPublicationResult { NewlyPublished, AlreadyPublishedIdentically }
public interface IEvidencePublicationPersistence { Task<ApprovedPublicationBatch?> LoadApprovedPublicationBatchAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken); Task<AtomicPublicationResult> PublishAtomicallyAsync(AtomicPublicationCommand command, CancellationToken cancellationToken); }

public sealed class PublicationInvariantException(string message) : InvalidOperationException(message);
public sealed class PublicationConflictException(string message) : InvalidOperationException(message);

public static class PublicationCommandFactory
{
    public static AtomicPublicationCommand Create(ApprovedPublicationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var snapshot = new ApprovedPublicationBatch(
            batch.WorkflowRunId,
            batch.WorkflowRunVersion,
            batch.WorkflowState,
            batch.ApprovalIdentity,
            batch.ApprovedAt,
            batch.ReviewerIdentity,
            batch.Source,
            batch.Claims.OrderBy(claim => claim.Ordinal),
            batch.EvidenceLinks
                .OrderBy(link => link.CandidateId.Value)
                .ThenBy(link => link.SourceRecordId.Value)
                .ThenBy(link => link.EvidenceType, StringComparer.Ordinal));
        var idempotency = $"{batch.WorkflowRunId.Value:N}:{batch.WorkflowRunVersion}";
        var canonical = JsonSerializer.Serialize(new
        {
            workflowRunId = snapshot.WorkflowRunId.Value.ToString("N"),
            snapshot.WorkflowRunVersion,
            workflowState = snapshot.WorkflowState.DatabaseValue,
            snapshot.ApprovalIdentity,
            approvedAt = snapshot.ApprovedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            snapshot.ReviewerIdentity,
            source = new
            {
                sourceRecordId = snapshot.Source.SourceRecordId.Value.ToString("N"),
                workflowRunId = snapshot.Source.WorkflowRunId.Value.ToString("N"),
                snapshot.Source.IdentityKey,
                snapshot.Source.Title,
                snapshot.Source.CanonicalUrl,
                snapshot.Source.SourceType
            },
            claims = snapshot.Claims.Select(claim => new
            {
                candidateId = claim.CandidateId.Value.ToString("N"),
                workflowRunId = claim.WorkflowRunId.Value.ToString("N"),
                sourceRecordId = claim.SourceRecordId.Value.ToString("N"),
                claim.Ordinal,
                claim.ClaimText,
                claim.StructuredCandidateJson,
                claim.DeterministicValidationJson,
                claim.ValidationPassed,
                claim.HumanApproved
            }),
            evidenceLinks = snapshot.EvidenceLinks.Select(link => new
            {
                candidateId = link.CandidateId.Value.ToString("N"),
                sourceRecordId = link.SourceRecordId.Value.ToString("N"),
                link.EvidenceType
            })
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new AtomicPublicationCommand(idempotency, fingerprint, snapshot);
    }
}
