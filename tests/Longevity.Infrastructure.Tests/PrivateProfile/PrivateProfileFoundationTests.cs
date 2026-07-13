using Longevity.Api.PrivateProfile;
using Longevity.Application.PrivateProfile;
using Longevity.Infrastructure.PrivateProfile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Longevity.Infrastructure.Tests.PrivateProfile;

public sealed class PrivateProfileFoundationTests
{
    [Fact]
    public async Task Profile_is_created_once_and_isolated_by_authenticated_subject()
    {
        var store = new FakeStore();
        var firstService = Service(store, "fictional-subject-a");
        var first = await firstService.GetOrCreateProfileAsync(CancellationToken.None);
        var updated = await firstService.UpdateProfileAsync(
            new(null, 1984, PrivateProfileValues.Female, PrivateProfileValues.Woman, PrivateProfileValues.Metric, "UTC"),
            CancellationToken.None);

        var second = await Service(store, "fictional-subject-b").GetOrCreateProfileAsync(CancellationToken.None);

        Assert.Equal(first.ProfileId, updated.ProfileId);
        Assert.NotEqual(first.ProfileId, second.ProfileId);
        Assert.Equal(1984, updated.BirthYear);
        Assert.Null(second.BirthYear);
        Assert.DoesNotContain("fictional-subject-a", updated.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthenticated_and_subjectless_requests_are_rejected_before_service_access()
    {
        var store = new FakeStore();
        var service = Service(store, "fictional-subject-a");

        var unauthenticated = await PrivateProfileEndpoints.GetProfileAsync(
            new TestCurrentUser(false, null), service, CancellationToken.None);
        var subjectless = await PrivateProfileEndpoints.GetProfileAsync(
            new TestCurrentUser(true, null), service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Status(unauthenticated));
        Assert.IsType<ForbidHttpResult>(subjectless);
        Assert.Empty(store.Profiles);
    }

    [Fact]
    public async Task Consent_defaults_are_declined_and_changes_are_append_only()
    {
        var service = Service(new FakeStore(), "fictional-subject-a");

        var defaults = await service.ListConsentsAsync(CancellationToken.None);
        var defaultResearch = Assert.Single(defaults, consent => consent.ConsentType == PrivateProfileValues.ResearchUse);
        var optionalCommercial = Assert.Single(defaults, consent => consent.ConsentType == PrivateProfileValues.CommercialPartnerMatching);
        Assert.Equal(PrivateProfileValues.Declined, defaultResearch.Status);
        Assert.Equal(PrivateProfileValues.Declined, optionalCommercial.Status);

        var granted = await service.RecordConsentAsync(
            new(PrivateProfileValues.ResearchUse, "2026-01", PrivateProfileValues.Granted, "fictional-web-test"),
            CancellationToken.None);
        var withdrawn = await service.WithdrawConsentAsync(
            PrivateProfileValues.ResearchUse,
            new("2026-02", "fictional-web-test"),
            CancellationToken.None);
        var history = await service.ListConsentsAsync(CancellationToken.None);

        Assert.Equal(PrivateProfileValues.Granted, granted.Status);
        Assert.Equal(PrivateProfileValues.Declined, withdrawn.Status);
        Assert.NotNull(withdrawn.WithdrawnAt);
        Assert.Contains(history, consent => consent.ConsentId == granted.ConsentId);
        Assert.Contains(history, consent => consent.ConsentId == withdrawn.ConsentId);
    }

    [Fact]
    public async Task Measurement_and_lab_validation_preserves_numeric_and_text_shapes()
    {
        var service = Service(new FakeStore(), "fictional-subject-a");
        var observedAt = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.AddMeasurementAsync(
            new(PrivateProfileValues.Weight, 0, "kg", observedAt, PrivateProfileValues.SelfReported, null),
            CancellationToken.None));

        var numeric = await service.AddLabAsync(
            new("Fictional marker A", "LOINC", "1234-5", 4.25m, null, "mg/L", 1m, 8m, "normal", "fictional panel", observedAt, PrivateProfileValues.LabReport, "fictional lab"),
            CancellationToken.None);
        var text = await service.AddLabAsync(
            new("Fictional marker B", null, null, null, "negative", null, null, null, null, null, observedAt, PrivateProfileValues.LabReport, null),
            CancellationToken.None);

        Assert.Equal(4.25m, numeric.NumericValue);
        Assert.Null(numeric.TextValue);
        Assert.Null(text.NumericValue);
        Assert.Equal("negative", text.TextValue);
    }

    [Fact]
    public async Task Observation_pagination_is_bounded_and_deterministically_ordered()
    {
        var service = Service(new FakeStore(), "fictional-subject-a");
        var firstAt = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        await service.AddMeasurementAsync(new(PrivateProfileValues.Weight, 70, "kg", firstAt, PrivateProfileValues.SelfReported, null), CancellationToken.None);
        await service.AddMeasurementAsync(new(PrivateProfileValues.Weight, 71, "kg", firstAt.AddDays(1), PrivateProfileValues.SelfReported, null), CancellationToken.None);
        await service.AddMeasurementAsync(new(PrivateProfileValues.Weight, 72, "kg", firstAt.AddDays(2), PrivateProfileValues.SelfReported, null), CancellationToken.None);

        var pageOne = await service.ListMeasurementsAsync(2, null, CancellationToken.None);
        var pageTwo = await service.ListMeasurementsAsync(2, pageOne.NextCursor, CancellationToken.None);

        Assert.Equal(new[] { 72m, 71m }, pageOne.Items.Select(item => item.NumericValue));
        Assert.Single(pageTwo.Items);
        Assert.Equal(70m, pageTwo.Items[0].NumericValue);
        Assert.Null(pageTwo.NextCursor);
    }

    [Fact]
    public async Task Preferences_and_goals_use_bounded_values_and_owner_scoping()
    {
        var store = new FakeStore();
        var owner = Service(store, "fictional-subject-a");
        var other = Service(store, "fictional-subject-b");

        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => owner.UpsertPreferencesAsync(
            new(12m, "US", PrivateProfileValues.Low, PrivateProfileValues.Balanced, PrivateProfileValues.LowerCost, null),
            CancellationToken.None));

        var preferences = await owner.UpsertPreferencesAsync(
            new(125m, "USD", PrivateProfileValues.Medium, PrivateProfileValues.Highest, PrivateProfileValues.Balanced, PrivateProfileValues.NoPreference),
            CancellationToken.None);
        var goal = await owner.AddGoalAsync(
            new(PrivateProfileValues.MetabolicHealth, null, 1),
            CancellationToken.None);

        await Assert.ThrowsAsync<PrivateProfileNotFoundException>(() => other.UpdateGoalAsync(
            goal.GoalId, new(null, false, null), CancellationToken.None));

        Assert.Equal("USD", preferences.PreferredCurrency);
        Assert.Equal(PrivateProfileValues.MetabolicHealth, goal.GoalType);
    }

    [Fact]
    public async Task Disabled_postgres_never_returns_fake_persistence()
    {
        var store = new DisabledPrivateProfileStore();

        await Assert.ThrowsAsync<PrivateProfilePersistenceUnavailableException>(() => store.GetOrCreateProfileAsync("fictional-subject", CancellationToken.None));
    }

    [Fact]
    public async Task Unexpected_failures_are_sanitized_without_private_values()
    {
        var service = Service(new ThrowingStore(), "fictional-subject-a");
        var result = await PrivateProfileEndpoints.GetProfileAsync(
            new TestCurrentUser(true, "fictional-subject-a"), service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, Status(result));
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        Assert.DoesNotContain("987.65", value?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fictional lab", value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_contains_private_boundary_rls_and_foreign_keys()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var migration = await File.ReadAllTextAsync(Path.Combine(root, "supabase", "migrations", "20260713000000_private_profile_foundation.sql"));

        Assert.Contains("create schema if not exists private_profile", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enable row level security", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("force row level security", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("references private_profile.profiles(profile_id) on delete cascade", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create index private_body_measurements_profile_observed_idx", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public.claims", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workflow.", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Private_profile_routes_require_authorization_and_do_not_take_profile_id()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.MapPrivateProfileApi();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/v1/me", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(routes);
        Assert.All(routes, route => Assert.NotEmpty(route.Metadata.GetOrderedMetadata<IAuthorizeData>()));
        Assert.DoesNotContain(routes, route => route.RoutePattern.RawText?.Contains("{profileId", StringComparison.Ordinal) == true);
    }

    private static PrivateProfileService Service(IPrivateProfileStore store, string subject) =>
        new(store, new TestCurrentUser(true, subject), TimeProvider.System);

    private static int Status(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;

    private sealed record TestCurrentUser(bool IsAuthenticated, string? SubjectId) : ICurrentUserContext;

    private sealed class ThrowingStore : FakeStore
    {
        public override Task<ProfileRecord> GetOrCreateProfileAsync(string externalSubjectId, CancellationToken cancellationToken) =>
            Task.FromException<ProfileRecord>(new Exception("fictional lab value 987.65"));
    }

    private class FakeStore : IPrivateProfileStore
    {
        public Dictionary<string, ProfileRecord> Profiles { get; } = new(StringComparer.Ordinal);
        private readonly List<ConsentRecord> consents = [];
        private readonly List<BodyMeasurementRecord> measurements = [];
        private readonly List<LabObservationRecord> labs = [];
        private readonly Dictionary<Guid, PreferenceRecord> preferences = [];
        private readonly List<GoalRecord> goals = [];

        public virtual Task<ProfileRecord> GetOrCreateProfileAsync(string externalSubjectId, CancellationToken cancellationToken)
        {
            if (!Profiles.TryGetValue(externalSubjectId, out var profile))
            {
                var now = DateTimeOffset.UtcNow;
                profile = new(Guid.NewGuid(), externalSubjectId, null, null, null, null, PrivateProfileValues.Metric, "UTC", now, now);
                Profiles.Add(externalSubjectId, profile);
                foreach (var type in PrivateProfileValues.ConsentTypes)
                    consents.Add(new(Guid.NewGuid(), profile.ProfileId, type, "foundation-v1", PrivateProfileValues.Declined, null, null, "system_default", now));
            }
            return Task.FromResult(profile);
        }

        public async Task<ProfileRecord> UpdateProfileAsync(string externalSubjectId, ProfileUpdateData update, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var updated = profile with
            {
                BirthDate = update.BirthDate,
                BirthYear = update.BirthYear,
                SexAtBirth = update.SexAtBirth,
                Gender = update.Gender,
                PreferredMeasurementSystem = update.PreferredMeasurementSystem,
                TimeZone = update.TimeZone,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Profiles[externalSubjectId] = updated;
            return updated;
        }

        public async Task<IReadOnlyList<ConsentRecord>> ListConsentsAsync(string externalSubjectId, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            return consents.Where(item => item.ProfileId == profile.ProfileId).OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.ConsentId).ToArray();
        }

        public async Task<ConsentRecord> AppendConsentAsync(string externalSubjectId, RecordConsentRequest request, DateTimeOffset? grantedAt, DateTimeOffset? withdrawnAt, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var item = new ConsentRecord(Guid.NewGuid(), profile.ProfileId, request.ConsentType, request.PolicyVersion, request.Status, grantedAt, withdrawnAt, request.CollectionSource, DateTimeOffset.UtcNow);
            consents.Add(item);
            return item;
        }

        public async Task<CursorPage<BodyMeasurementRecord>> ListMeasurementsAsync(string externalSubjectId, ObservationCursor? cursor, int limit, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var query = measurements.Where(item => item.ProfileId == profile.ProfileId).OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.ObservationId);
            if (cursor is not null) query = query.Where(item => item.ObservedAt < cursor.ObservedAt || (item.ObservedAt == cursor.ObservedAt && item.ObservationId.CompareTo(cursor.ObservationId) < 0)).OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.ObservationId);
            var items = query.Take(limit + 1).ToList();
            var next = items.Count > limit ? new ObservationCursor(items[limit - 1].ObservedAt, items[limit - 1].ObservationId) : null;
            if (items.Count > limit) items.RemoveAt(limit);
            return new(items, next);
        }

        public async Task<BodyMeasurementRecord> AddMeasurementAsync(string externalSubjectId, BodyMeasurementRequest request, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var item = new BodyMeasurementRecord(Guid.NewGuid(), profile.ProfileId, request.MeasurementType, request.NumericValue, request.Unit, request.ObservedAt, request.SourceType, request.SourceLabel, DateTimeOffset.UtcNow);
            measurements.Add(item);
            return item;
        }

        public async Task<CursorPage<LabObservationRecord>> ListLabsAsync(string externalSubjectId, ObservationCursor? cursor, int limit, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var query = labs.Where(item => item.ProfileId == profile.ProfileId).OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.ObservationId);
            if (cursor is not null) query = query.Where(item => item.ObservedAt < cursor.ObservedAt || (item.ObservedAt == cursor.ObservedAt && item.ObservationId.CompareTo(cursor.ObservationId) < 0)).OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.ObservationId);
            var items = query.Take(limit + 1).ToList();
            var next = items.Count > limit ? new ObservationCursor(items[limit - 1].ObservedAt, items[limit - 1].ObservationId) : null;
            if (items.Count > limit) items.RemoveAt(limit);
            return new(items, next);
        }

        public async Task<LabObservationRecord> AddLabAsync(string externalSubjectId, LabObservationRequest request, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var item = new LabObservationRecord(Guid.NewGuid(), profile.ProfileId, request.TestName, request.StandardizedCodeSystem, request.StandardizedTestCode, request.NumericValue, request.TextValue, request.Unit, request.ReferenceRangeMinimum, request.ReferenceRangeMaximum, request.AbnormalFlag, request.SpecimenOrPanelLabel, request.ObservedAt, request.SourceType, request.SourceLabel, DateTimeOffset.UtcNow);
            labs.Add(item);
            return item;
        }

        public async Task<PreferenceRecord?> GetPreferencesAsync(string externalSubjectId, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            return preferences.GetValueOrDefault(profile.ProfileId);
        }

        public async Task<PreferenceRecord> UpsertPreferencesAsync(string externalSubjectId, PreferencesRequest request, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var item = new PreferenceRecord(profile.ProfileId, request.MonthlyLongevityBudget, request.PreferredCurrency, request.RiskTolerance, request.EvidenceConfidencePreference, request.CostVersusConfidencePreference, request.InsuranceUsePreference, DateTimeOffset.UtcNow);
            preferences[profile.ProfileId] = item;
            return item;
        }

        public async Task<IReadOnlyList<GoalRecord>> ListGoalsAsync(string externalSubjectId, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            return goals.Where(item => item.ProfileId == profile.ProfileId).OrderByDescending(item => item.Active).ThenBy(item => item.Priority).ThenBy(item => item.CreatedAt).ThenBy(item => item.GoalId).ToArray();
        }

        public async Task<GoalRecord> AddGoalAsync(string externalSubjectId, CreateGoalRequest request, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var item = new GoalRecord(Guid.NewGuid(), profile.ProfileId, request.GoalType, request.UserLabel, request.Priority, request.Active, now, now);
            goals.Add(item);
            return item;
        }

        public async Task<GoalRecord?> UpdateGoalAsync(string externalSubjectId, Guid goalId, GoalUpdateData update, CancellationToken cancellationToken)
        {
            var profile = await GetOrCreateProfileAsync(externalSubjectId, cancellationToken);
            var index = goals.FindIndex(item => item.GoalId == goalId && item.ProfileId == profile.ProfileId);
            if (index < 0) return null;
            var existing = goals[index] with
            {
                Priority = update.Priority ?? goals[index].Priority,
                Active = update.Active ?? goals[index].Active,
                UserLabel = update.UserLabel ?? goals[index].UserLabel,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            goals[index] = existing;
            return existing;
        }
    }
}
