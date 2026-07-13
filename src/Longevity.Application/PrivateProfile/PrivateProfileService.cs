using System.Globalization;
using System.Text.RegularExpressions;

namespace Longevity.Application.PrivateProfile;

public sealed class PrivateProfileService(
    IPrivateProfileStore store,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
    : IPrivateProfileService, IPrivateConsentService, IPrivateObservationService, IPrivatePreferenceService, IPrivateGoalService
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private static readonly Regex CurrencyCode = new("^[A-Z]{3}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<ProfileResponse> GetOrCreateProfileAsync(CancellationToken cancellationToken)
    {
        var subject = Subject();
        return ToResponse(await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ProfileResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subject = Subject();
        var update = ValidateProfile(request);
        return ToResponse(await store.UpdateProfileAsync(subject, update, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<ConsentResponse>> ListConsentsAsync(CancellationToken cancellationToken)
    {
        var subject = Subject();
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);
        var records = await store.ListConsentsAsync(subject, cancellationToken).ConfigureAwait(false);
        return records.Select(ToResponse).ToArray();
    }

    public async Task<ConsentResponse> RecordConsentAsync(RecordConsentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subject = Subject();
        ValidateConsent(request.ConsentType, request.PolicyVersion, request.Status, request.CollectionSource);
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);

        DateTimeOffset? grantedAt = request.Status == PrivateProfileValues.Granted ? timeProvider.GetUtcNow() : null;
        var record = await store.AppendConsentAsync(subject, NormalizeRequest(request), grantedAt, null, cancellationToken).ConfigureAwait(false);
        return ToResponse(record);
    }

    public async Task<ConsentResponse> WithdrawConsentAsync(
        string consentType,
        WithdrawConsentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subject = Subject();
        ValidateConsent(consentType, request.PolicyVersion, PrivateProfileValues.Declined, request.CollectionSource);
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);

        var consent = new RecordConsentRequest(
            Normalize(consentType, nameof(consentType)),
            NormalizeRequired(request.PolicyVersion, nameof(request.PolicyVersion), 64),
            PrivateProfileValues.Declined,
            NormalizeRequired(request.CollectionSource, nameof(request.CollectionSource), 64));
        var record = await store.AppendConsentAsync(
            subject,
            consent,
            null,
            timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return ToResponse(record);
    }

    public async Task<ObservationPageResponse<BodyMeasurementResponse>> ListMeasurementsAsync(
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var subject = Subject();
        var page = await store.ListMeasurementsAsync(subject, ParsePage(limit, cursor), ClampPageSize(limit), cancellationToken).ConfigureAwait(false);
        return new(page.Items.Select(ToResponse).ToArray(), page.NextCursor is null ? null : PrivateProfileCursorCodec.Encode(page.NextCursor));
    }

    public async Task<BodyMeasurementResponse> AddMeasurementAsync(
        BodyMeasurementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subject = Subject();
        var normalized = ValidateMeasurement(request);
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);
        return ToResponse(await store.AddMeasurementAsync(subject, normalized, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ObservationPageResponse<LabObservationResponse>> ListLabsAsync(
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var subject = Subject();
        var page = await store.ListLabsAsync(subject, ParsePage(limit, cursor), ClampPageSize(limit), cancellationToken).ConfigureAwait(false);
        return new(page.Items.Select(ToResponse).ToArray(), page.NextCursor is null ? null : PrivateProfileCursorCodec.Encode(page.NextCursor));
    }

    public async Task<LabObservationResponse> AddLabAsync(
        LabObservationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subject = Subject();
        var normalized = ValidateLab(request);
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);
        return ToResponse(await store.AddLabAsync(subject, normalized, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PreferencesResponse?> GetPreferencesAsync(CancellationToken cancellationToken)
    {
        var subject = Subject();
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);
        var record = await store.GetPreferencesAsync(subject, cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToResponse(record);
    }

    public async Task<PreferencesResponse> UpsertPreferencesAsync(
        PreferencesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subject = Subject();
        var normalized = ValidatePreferences(request);
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);
        return ToResponse(await store.UpsertPreferencesAsync(subject, normalized, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<GoalResponse>> ListGoalsAsync(CancellationToken cancellationToken)
    {
        var subject = Subject();
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);
        var records = await store.ListGoalsAsync(subject, cancellationToken).ConfigureAwait(false);
        return records.Select(ToResponse).ToArray();
    }

    public async Task<GoalResponse> AddGoalAsync(
        CreateGoalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subject = Subject();
        var normalized = ValidateGoal(request);
        _ = await store.GetOrCreateProfileAsync(subject, cancellationToken).ConfigureAwait(false);
        return ToResponse(await store.AddGoalAsync(subject, normalized, cancellationToken).ConfigureAwait(false));
    }

    public async Task<GoalResponse> UpdateGoalAsync(
        Guid goalId,
        UpdateGoalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (goalId == Guid.Empty) throw new PrivateProfileValidationException("goalId", "Goal identity must be non-empty.");
        if (request.Priority is null && request.Active is null && request.UserLabel is null)
            throw new PrivateProfileValidationException("request", "At least one goal field must be provided.");
        if (request.Priority is < 1 or > 5)
            throw new PrivateProfileValidationException("priority", "Priority must be between 1 and 5.");
        var label = request.UserLabel is null ? null : NormalizeOptional(request.UserLabel, "userLabel", 200);
        var subject = Subject();
        var record = await store.UpdateGoalAsync(subject, goalId, new(request.Priority, request.Active, label), cancellationToken).ConfigureAwait(false);
        return record is null ? throw new PrivateProfileNotFoundException() : ToResponse(record);
    }

    private string Subject() => PrivateProfileSubject.Require(currentUser);

    private static ProfileUpdateData ValidateProfile(UpdateProfileRequest request)
    {
        if (request.BirthDate is not null && request.BirthYear is not null)
            throw new PrivateProfileValidationException("birthDate", "Provide birthDate or birthYear, not both.");
        if (request.BirthDate is { } birthDate && birthDate > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new PrivateProfileValidationException("birthDate", "Birth date cannot be in the future.");
        if (request.BirthYear is < 1900 or > 2100)
            throw new PrivateProfileValidationException("birthYear", "Birth year must be between 1900 and 2100.");

        var measurementSystem = NormalizeRequired(request.PreferredMeasurementSystem, "preferredMeasurementSystem", 32);
        if (!PrivateProfileValues.MeasurementSystems.Contains(measurementSystem))
            throw new PrivateProfileValidationException("preferredMeasurementSystem", "Measurement system must be metric or imperial.");

        var timeZone = NormalizeRequired(request.TimeZone, "timeZone", 64);
        var sexAtBirth = NormalizeOptional(request.SexAtBirth, "sexAtBirth", 32);
        if (sexAtBirth is not null && !PrivateProfileValues.SexAtBirthValues.Contains(sexAtBirth))
            throw new PrivateProfileValidationException("sexAtBirth", "Sex-at-birth value is not supported.");
        var gender = NormalizeOptional(request.Gender, "gender", 32);
        if (gender is not null && !PrivateProfileValues.GenderValues.Contains(gender))
            throw new PrivateProfileValidationException("gender", "Gender value is not supported.");

        return new(request.BirthDate, request.BirthYear, sexAtBirth, gender, measurementSystem, timeZone);
    }

    private static void ValidateConsent(string? consentType, string? policyVersion, string? status, string? collectionSource)
    {
        var normalizedType = NormalizeRequired(consentType, "consentType", 64);
        if (!PrivateProfileValues.ConsentTypes.Contains(normalizedType))
            throw new PrivateProfileValidationException("consentType", "Consent type is not supported.");
        var normalizedStatus = NormalizeRequired(status, "status", 16);
        if (normalizedStatus is not PrivateProfileValues.Granted and not PrivateProfileValues.Declined)
            throw new PrivateProfileValidationException("status", "Consent status must be granted or declined.");
        _ = NormalizeRequired(policyVersion, "policyVersion", 64);
        _ = NormalizeRequired(collectionSource, "collectionSource", 64);
    }

    private static RecordConsentRequest NormalizeRequest(RecordConsentRequest request) =>
        new(
            NormalizeRequired(request.ConsentType, nameof(request.ConsentType), 64),
            NormalizeRequired(request.PolicyVersion, nameof(request.PolicyVersion), 64),
            NormalizeRequired(request.Status, nameof(request.Status), 16),
            NormalizeRequired(request.CollectionSource, nameof(request.CollectionSource), 64));

    private static BodyMeasurementRequest ValidateMeasurement(BodyMeasurementRequest request)
    {
        var type = NormalizeRequired(request.MeasurementType, nameof(request.MeasurementType), 100);
        var unit = NormalizeRequired(request.Unit, nameof(request.Unit), 32);
        if (request.NumericValue <= 0)
            throw new PrivateProfileValidationException("numericValue", "Measurement value must be greater than zero.");
        if (type == PrivateProfileValues.BodyFatPercentage && request.NumericValue > 100)
            throw new PrivateProfileValidationException("numericValue", "Body-fat percentage must be between 0 and 100.");
        if (request.ObservedAt == default)
            throw new PrivateProfileValidationException("observedAt", "Observation timestamp is required.");
        var sourceType = NormalizeRequired(request.SourceType, nameof(request.SourceType), 32);
        if (sourceType is not PrivateProfileValues.SelfReported
            and not PrivateProfileValues.LabReport
            and not PrivateProfileValues.Clinician
            and not PrivateProfileValues.Imported
            and not PrivateProfileValues.Other)
            throw new PrivateProfileValidationException("sourceType", "Measurement source type is not supported.");
        return request with
        {
            MeasurementType = type,
            Unit = unit,
            SourceType = sourceType,
            SourceLabel = NormalizeOptional(request.SourceLabel, nameof(request.SourceLabel), 200)
        };
    }

    private static LabObservationRequest ValidateLab(LabObservationRequest request)
    {
        var testName = NormalizeRequired(request.TestName, nameof(request.TestName), 200);
        if ((request.NumericValue is null) == (string.IsNullOrWhiteSpace(request.TextValue)))
            throw new PrivateProfileValidationException("value", "Provide exactly one numericValue or textValue.");
        if (request.ReferenceRangeMinimum is not null
            && request.ReferenceRangeMaximum is not null
            && request.ReferenceRangeMinimum > request.ReferenceRangeMaximum)
            throw new PrivateProfileValidationException("referenceRangeMinimum", "Reference-range minimum cannot exceed maximum.");
        var codeSystem = NormalizeOptional(request.StandardizedCodeSystem, nameof(request.StandardizedCodeSystem), 64);
        var code = NormalizeOptional(request.StandardizedTestCode, nameof(request.StandardizedTestCode), 64);
        if (code is not null && codeSystem is null)
            throw new PrivateProfileValidationException("standardizedCodeSystem", "A code system is required when a standardized test code is provided.");
        if (request.ObservedAt == default)
            throw new PrivateProfileValidationException("observedAt", "Observation timestamp is required.");
        var sourceType = NormalizeRequired(request.SourceType, nameof(request.SourceType), 32);
        if (!PrivateProfileValues.LabSourceTypes.Contains(sourceType))
            throw new PrivateProfileValidationException("sourceType", "Lab source type is not supported.");
        return request with
        {
            TestName = testName,
            StandardizedCodeSystem = codeSystem,
            StandardizedTestCode = code,
            TextValue = NormalizeOptional(request.TextValue, nameof(request.TextValue), 2000),
            Unit = NormalizeOptional(request.Unit, nameof(request.Unit), 32),
            AbnormalFlag = NormalizeOptional(request.AbnormalFlag, nameof(request.AbnormalFlag), 16),
            SpecimenOrPanelLabel = NormalizeOptional(request.SpecimenOrPanelLabel, nameof(request.SpecimenOrPanelLabel), 200),
            SourceType = sourceType,
            SourceLabel = NormalizeOptional(request.SourceLabel, nameof(request.SourceLabel), 200)
        };
    }

    private static PreferencesRequest ValidatePreferences(PreferencesRequest request)
    {
        if (request.MonthlyLongevityBudget < 0 || request.MonthlyLongevityBudget > 1_000_000)
            throw new PrivateProfileValidationException("monthlyLongevityBudget", "Monthly budget must be between 0 and 1,000,000.");
        var currency = NormalizeRequired(request.PreferredCurrency, nameof(request.PreferredCurrency), 3).ToUpperInvariant();
        if (!CurrencyCode.IsMatch(currency))
            throw new PrivateProfileValidationException("preferredCurrency", "Currency must be a three-letter ISO-style code.");
        var risk = NormalizeRequired(request.RiskTolerance, nameof(request.RiskTolerance), 32);
        if (!PrivateProfileValues.RiskToleranceValues.Contains(risk))
            throw new PrivateProfileValidationException("riskTolerance", "Risk tolerance is not supported.");
        var evidence = NormalizeRequired(request.EvidenceConfidencePreference, nameof(request.EvidenceConfidencePreference), 32);
        if (!PrivateProfileValues.EvidenceConfidenceValues.Contains(evidence))
            throw new PrivateProfileValidationException("evidenceConfidencePreference", "Evidence-confidence preference is not supported.");
        var cost = NormalizeRequired(request.CostVersusConfidencePreference, nameof(request.CostVersusConfidencePreference), 32);
        if (!PrivateProfileValues.CostConfidenceValues.Contains(cost))
            throw new PrivateProfileValidationException("costVersusConfidencePreference", "Cost-versus-confidence preference is not supported.");
        var insurance = NormalizeOptional(request.InsuranceUsePreference, nameof(request.InsuranceUsePreference), 32);
        if (insurance is not null && !PrivateProfileValues.InsurancePreferenceValues.Contains(insurance))
            throw new PrivateProfileValidationException("insuranceUsePreference", "Insurance-use preference is not supported.");
        return new(request.MonthlyLongevityBudget, currency, risk, evidence, cost, insurance);
    }

    private static CreateGoalRequest ValidateGoal(CreateGoalRequest request)
    {
        var type = NormalizeRequired(request.GoalType, nameof(request.GoalType), 64);
        if (!PrivateProfileValues.GoalTypes.Contains(type))
            throw new PrivateProfileValidationException("goalType", "Goal type is not supported.");
        if (request.Priority is < 1 or > 5)
            throw new PrivateProfileValidationException("priority", "Priority must be between 1 and 5.");
        var label = NormalizeOptional(request.UserLabel, nameof(request.UserLabel), 200);
        if (type == PrivateProfileValues.UserDefined && label is null)
            throw new PrivateProfileValidationException("userLabel", "User-defined goals require a label.");
        return new(type, label, request.Priority, request.Active);
    }

    private static ObservationCursor? ParsePage(int limit, string? cursor)
    {
        _ = ClampPageSize(limit);
        if (!PrivateProfileCursorCodec.TryDecode(cursor, out var parsed))
            throw new PrivateProfileValidationException("cursor", "Cursor is invalid.");
        return parsed;
    }

    private static int ClampPageSize(int limit)
    {
        if (limit is < 1 or > MaxPageSize)
            throw new PrivateProfileValidationException("limit", $"Limit must be between 1 and {MaxPageSize}.");
        return limit == 0 ? DefaultPageSize : limit;
    }

    private static string NormalizeRequired(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PrivateProfileValidationException(field, "Value is required.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new PrivateProfileValidationException(field, $"Value cannot exceed {maxLength} characters.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new PrivateProfileValidationException(field, $"Value cannot exceed {maxLength} characters.");
        return normalized;
    }

    private static string Normalize(string? value, string field) => NormalizeRequired(value, field, 200);

    private static ProfileResponse ToResponse(ProfileRecord record) =>
        new(record.ProfileId, record.BirthDate, record.BirthYear, record.SexAtBirth, record.Gender,
            record.PreferredMeasurementSystem, record.TimeZone, record.CreatedAt, record.UpdatedAt);

    private static ConsentResponse ToResponse(ConsentRecord record) =>
        new(record.ConsentId, record.ConsentType, record.PolicyVersion, record.Status, record.GrantedAt,
            record.WithdrawnAt, record.CollectionSource, record.CreatedAt);

    private static BodyMeasurementResponse ToResponse(BodyMeasurementRecord record) =>
        new(record.ObservationId, record.MeasurementType, record.NumericValue, record.Unit, record.ObservedAt,
            record.SourceType, record.SourceLabel, record.CreatedAt);

    private static LabObservationResponse ToResponse(LabObservationRecord record) =>
        new(record.ObservationId, record.TestName, record.StandardizedCodeSystem, record.StandardizedTestCode,
            record.NumericValue, record.TextValue, record.Unit, record.ReferenceRangeMinimum,
            record.ReferenceRangeMaximum, record.AbnormalFlag, record.SpecimenOrPanelLabel, record.ObservedAt,
            record.SourceType, record.SourceLabel, record.CreatedAt);

    private static PreferencesResponse ToResponse(PreferenceRecord record) =>
        new(record.MonthlyLongevityBudget, record.PreferredCurrency, record.RiskTolerance,
            record.EvidenceConfidencePreference, record.CostVersusConfidencePreference,
            record.InsuranceUsePreference, record.UpdatedAt);

    private static GoalResponse ToResponse(GoalRecord record) =>
        new(record.GoalId, record.GoalType, record.UserLabel, record.Priority, record.Active,
            record.CreatedAt, record.UpdatedAt);
}
