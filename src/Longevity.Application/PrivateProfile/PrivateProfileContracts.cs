using System.Globalization;
using System.Text;

namespace Longevity.Application.PrivateProfile;

public static class PrivateProfileValues
{
    public const string Metric = "metric";
    public const string Imperial = "imperial";

    public const string Female = "female";
    public const string Male = "male";
    public const string Intersex = "intersex";
    public const string Unknown = "unknown";
    public const string NotDisclosed = "not_disclosed";

    public const string Woman = "woman";
    public const string Man = "man";
    public const string NonBinary = "non_binary";
    public const string GenderFluid = "gender_fluid";
    public const string Other = "other";

    public const string ProfileDataStorage = "profile_data_storage";
    public const string PersonalizedAnalysis = "personalized_analysis";
    public const string ResearchUse = "research_use";
    public const string DeidentifiedAggregateDataUse = "deidentified_aggregate_data_use";
    public const string CommercialPartnerMatching = "commercial_partner_matching";

    public const string Granted = "granted";
    public const string Declined = "declined";

    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Minimum = "minimum";
    public const string Balanced = "balanced";
    public const string Highest = "highest";
    public const string LowerCost = "lower_cost";
    public const string HigherConfidence = "higher_confidence";
    public const string PreferUse = "prefer_use";
    public const string AvoidUse = "avoid_use";
    public const string NoPreference = "no_preference";

    public const string Weight = "weight";
    public const string Height = "height";
    public const string WaistCircumference = "waist_circumference";
    public const string BodyFatPercentage = "body_fat_percentage";

    public const string SelfReported = "self_reported";
    public const string LabReport = "lab_report";
    public const string Clinician = "clinician";
    public const string Imported = "imported";

    public const string GeneralHealthyAging = "general_healthy_aging";
    public const string CardiovascularRiskReduction = "cardiovascular_risk_reduction";
    public const string MetabolicHealth = "metabolic_health";
    public const string CognitiveHealth = "cognitive_health";
    public const string MobilityAndPhysicalFunction = "mobility_and_physical_function";
    public const string Sleep = "sleep";
    public const string AppearanceOrSkinAging = "appearance_or_skin_aging";
    public const string UserDefined = "user_defined";

    public static readonly IReadOnlySet<string> ConsentTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        ProfileDataStorage, PersonalizedAnalysis, ResearchUse,
        DeidentifiedAggregateDataUse, CommercialPartnerMatching
    };

    public static readonly IReadOnlySet<string> MeasurementSystems = new HashSet<string>(StringComparer.Ordinal)
    {
        Metric, Imperial
    };

    public static readonly IReadOnlySet<string> SexAtBirthValues = new HashSet<string>(StringComparer.Ordinal)
    {
        Female, Male, Intersex, Unknown, NotDisclosed
    };

    public static readonly IReadOnlySet<string> GenderValues = new HashSet<string>(StringComparer.Ordinal)
    {
        Woman, Man, NonBinary, GenderFluid, Other, Unknown, NotDisclosed
    };

    public static readonly IReadOnlySet<string> RiskToleranceValues = new HashSet<string>(StringComparer.Ordinal)
    {
        Low, Medium, High
    };

    public static readonly IReadOnlySet<string> EvidenceConfidenceValues = new HashSet<string>(StringComparer.Ordinal)
    {
        Minimum, Balanced, Highest
    };

    public static readonly IReadOnlySet<string> CostConfidenceValues = new HashSet<string>(StringComparer.Ordinal)
    {
        LowerCost, Balanced, HigherConfidence
    };

    public static readonly IReadOnlySet<string> InsurancePreferenceValues = new HashSet<string>(StringComparer.Ordinal)
    {
        PreferUse, AvoidUse, NoPreference
    };

    public static readonly IReadOnlySet<string> GoalTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        GeneralHealthyAging, CardiovascularRiskReduction, MetabolicHealth,
        CognitiveHealth, MobilityAndPhysicalFunction, Sleep,
        AppearanceOrSkinAging, UserDefined
    };

    public static readonly IReadOnlySet<string> LabSourceTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        SelfReported, LabReport, Clinician, Imported, Other
    };
}

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    string? SubjectId { get; }
}

public sealed record ProfileRecord(
    Guid ProfileId,
    string ExternalSubjectId,
    DateOnly? BirthDate,
    int? BirthYear,
    string? SexAtBirth,
    string? Gender,
    string PreferredMeasurementSystem,
    string TimeZone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConsentRecord(
    Guid ConsentId,
    Guid ProfileId,
    string ConsentType,
    string PolicyVersion,
    string Status,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? WithdrawnAt,
    string CollectionSource,
    DateTimeOffset CreatedAt);

public sealed record BodyMeasurementRecord(
    Guid ObservationId,
    Guid ProfileId,
    string MeasurementType,
    decimal NumericValue,
    string Unit,
    DateTimeOffset ObservedAt,
    string SourceType,
    string? SourceLabel,
    DateTimeOffset CreatedAt);

public sealed record LabObservationRecord(
    Guid ObservationId,
    Guid ProfileId,
    string TestName,
    string? StandardizedCodeSystem,
    string? StandardizedTestCode,
    decimal? NumericValue,
    string? TextValue,
    string? Unit,
    decimal? ReferenceRangeMinimum,
    decimal? ReferenceRangeMaximum,
    string? AbnormalFlag,
    string? SpecimenOrPanelLabel,
    DateTimeOffset ObservedAt,
    string SourceType,
    string? SourceLabel,
    DateTimeOffset CreatedAt);

public sealed record PreferenceRecord(
    Guid ProfileId,
    decimal MonthlyLongevityBudget,
    string PreferredCurrency,
    string RiskTolerance,
    string EvidenceConfidencePreference,
    string CostVersusConfidencePreference,
    string? InsuranceUsePreference,
    DateTimeOffset UpdatedAt);

public sealed record GoalRecord(
    Guid GoalId,
    Guid ProfileId,
    string GoalType,
    string? UserLabel,
    int Priority,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpdateProfileRequest(
    DateOnly? BirthDate,
    int? BirthYear,
    string? SexAtBirth,
    string? Gender,
    string? PreferredMeasurementSystem,
    string? TimeZone);

public sealed record RecordConsentRequest(
    string ConsentType,
    string PolicyVersion,
    string Status,
    string CollectionSource);

public sealed record WithdrawConsentRequest(
    string PolicyVersion,
    string CollectionSource);

public sealed record BodyMeasurementRequest(
    string MeasurementType,
    decimal NumericValue,
    string Unit,
    DateTimeOffset ObservedAt,
    string SourceType,
    string? SourceLabel);

public sealed record LabObservationRequest(
    string TestName,
    string? StandardizedCodeSystem,
    string? StandardizedTestCode,
    decimal? NumericValue,
    string? TextValue,
    string? Unit,
    decimal? ReferenceRangeMinimum,
    decimal? ReferenceRangeMaximum,
    string? AbnormalFlag,
    string? SpecimenOrPanelLabel,
    DateTimeOffset ObservedAt,
    string SourceType,
    string? SourceLabel);

public sealed record PreferencesRequest(
    decimal MonthlyLongevityBudget,
    string PreferredCurrency,
    string RiskTolerance,
    string EvidenceConfidencePreference,
    string CostVersusConfidencePreference,
    string? InsuranceUsePreference);

public sealed record CreateGoalRequest(
    string GoalType,
    string? UserLabel,
    int Priority,
    bool Active = true);

public sealed record UpdateGoalRequest(
    int? Priority,
    bool? Active,
    string? UserLabel);

public sealed record ObservationCursor(DateTimeOffset ObservedAt, Guid ObservationId);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, ObservationCursor? NextCursor);

public sealed record ProfileResponse(
    Guid ProfileId,
    DateOnly? BirthDate,
    int? BirthYear,
    string? SexAtBirth,
    string? Gender,
    string PreferredMeasurementSystem,
    string TimeZone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConsentResponse(
    Guid ConsentId,
    string ConsentType,
    string PolicyVersion,
    string Status,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? WithdrawnAt,
    string CollectionSource,
    DateTimeOffset CreatedAt);

public sealed record BodyMeasurementResponse(
    Guid ObservationId,
    string MeasurementType,
    decimal NumericValue,
    string Unit,
    DateTimeOffset ObservedAt,
    string SourceType,
    string? SourceLabel,
    DateTimeOffset CreatedAt);

public sealed record LabObservationResponse(
    Guid ObservationId,
    string TestName,
    string? StandardizedCodeSystem,
    string? StandardizedTestCode,
    decimal? NumericValue,
    string? TextValue,
    string? Unit,
    decimal? ReferenceRangeMinimum,
    decimal? ReferenceRangeMaximum,
    string? AbnormalFlag,
    string? SpecimenOrPanelLabel,
    DateTimeOffset ObservedAt,
    string SourceType,
    string? SourceLabel,
    DateTimeOffset CreatedAt);

public sealed record PreferencesResponse(
    decimal MonthlyLongevityBudget,
    string PreferredCurrency,
    string RiskTolerance,
    string EvidenceConfidencePreference,
    string CostVersusConfidencePreference,
    string? InsuranceUsePreference,
    DateTimeOffset UpdatedAt);

public sealed record GoalResponse(
    Guid GoalId,
    string GoalType,
    string? UserLabel,
    int Priority,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ObservationPageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record ProfileUpdateData(
    DateOnly? BirthDate,
    int? BirthYear,
    string? SexAtBirth,
    string? Gender,
    string PreferredMeasurementSystem,
    string TimeZone);

public sealed record GoalUpdateData(int? Priority, bool? Active, string? UserLabel);

public interface IPrivateProfileStore
{
    Task<ProfileRecord> GetOrCreateProfileAsync(string externalSubjectId, CancellationToken cancellationToken);
    Task<ProfileRecord> UpdateProfileAsync(string externalSubjectId, ProfileUpdateData update, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConsentRecord>> ListConsentsAsync(string externalSubjectId, CancellationToken cancellationToken);
    Task<ConsentRecord> AppendConsentAsync(string externalSubjectId, RecordConsentRequest request, DateTimeOffset? grantedAt, DateTimeOffset? withdrawnAt, CancellationToken cancellationToken);

    Task<CursorPage<BodyMeasurementRecord>> ListMeasurementsAsync(string externalSubjectId, ObservationCursor? cursor, int limit, CancellationToken cancellationToken);
    Task<BodyMeasurementRecord> AddMeasurementAsync(string externalSubjectId, BodyMeasurementRequest request, CancellationToken cancellationToken);

    Task<CursorPage<LabObservationRecord>> ListLabsAsync(string externalSubjectId, ObservationCursor? cursor, int limit, CancellationToken cancellationToken);
    Task<LabObservationRecord> AddLabAsync(string externalSubjectId, LabObservationRequest request, CancellationToken cancellationToken);

    Task<PreferenceRecord?> GetPreferencesAsync(string externalSubjectId, CancellationToken cancellationToken);
    Task<PreferenceRecord> UpsertPreferencesAsync(string externalSubjectId, PreferencesRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<GoalRecord>> ListGoalsAsync(string externalSubjectId, CancellationToken cancellationToken);
    Task<GoalRecord> AddGoalAsync(string externalSubjectId, CreateGoalRequest request, CancellationToken cancellationToken);
    Task<GoalRecord?> UpdateGoalAsync(string externalSubjectId, Guid goalId, GoalUpdateData update, CancellationToken cancellationToken);
}

public interface IPrivateProfileService
{
    Task<ProfileResponse> GetOrCreateProfileAsync(CancellationToken cancellationToken);
    Task<ProfileResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken);
}

public interface IPrivateConsentService
{
    Task<IReadOnlyList<ConsentResponse>> ListConsentsAsync(CancellationToken cancellationToken);
    Task<ConsentResponse> RecordConsentAsync(RecordConsentRequest request, CancellationToken cancellationToken);
    Task<ConsentResponse> WithdrawConsentAsync(string consentType, WithdrawConsentRequest request, CancellationToken cancellationToken);
}

public interface IPrivateObservationService
{
    Task<ObservationPageResponse<BodyMeasurementResponse>> ListMeasurementsAsync(int limit, string? cursor, CancellationToken cancellationToken);
    Task<BodyMeasurementResponse> AddMeasurementAsync(BodyMeasurementRequest request, CancellationToken cancellationToken);
    Task<ObservationPageResponse<LabObservationResponse>> ListLabsAsync(int limit, string? cursor, CancellationToken cancellationToken);
    Task<LabObservationResponse> AddLabAsync(LabObservationRequest request, CancellationToken cancellationToken);
}

public interface IPrivatePreferenceService
{
    Task<PreferencesResponse?> GetPreferencesAsync(CancellationToken cancellationToken);
    Task<PreferencesResponse> UpsertPreferencesAsync(PreferencesRequest request, CancellationToken cancellationToken);
}

public interface IPrivateGoalService
{
    Task<IReadOnlyList<GoalResponse>> ListGoalsAsync(CancellationToken cancellationToken);
    Task<GoalResponse> AddGoalAsync(CreateGoalRequest request, CancellationToken cancellationToken);
    Task<GoalResponse> UpdateGoalAsync(Guid goalId, UpdateGoalRequest request, CancellationToken cancellationToken);
}

public sealed class PrivateProfileValidationException : Exception
{
    public PrivateProfileValidationException(string field, string message)
        : base("The private-profile request is invalid.")
    {
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [message]
        };
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class PrivateProfilePersistenceUnavailableException : Exception
{
    public PrivateProfilePersistenceUnavailableException() : base("Private-profile persistence is unavailable.") { }
}

public sealed class PrivateProfileConflictException : Exception
{
    public PrivateProfileConflictException() : base("The private-profile operation conflicts with current state.") { }
}

public sealed class PrivateProfileNotFoundException : Exception
{
    public PrivateProfileNotFoundException() : base("The private-profile resource was not found.") { }
}

public static class PrivateProfileSubject
{
    public static string Require(ICurrentUserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsAuthenticated)
        {
            throw new UnauthorizedAccessException();
        }

        if (string.IsNullOrWhiteSpace(context.SubjectId))
        {
            throw new InvalidOperationException("The authenticated subject is missing.");
        }

        return context.SubjectId.Trim();
    }
}

public static class PrivateProfileCursorCodec
{
    public static string Encode(ObservationCursor cursor)
    {
        var value = $"{cursor.ObservedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{cursor.ObservationId:D}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, out ObservationCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(normalized)).Split('|', 2);
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParse(parts[1], out var observationId)
                || observationId == Guid.Empty
                || ticks < DateTimeOffset.MinValue.UtcTicks
                || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                return false;
            }

            cursor = new ObservationCursor(new DateTimeOffset(ticks, TimeSpan.Zero), observationId);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
