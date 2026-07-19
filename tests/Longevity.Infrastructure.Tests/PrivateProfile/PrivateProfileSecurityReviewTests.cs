using System.Security.Claims;
using System.Text;
using Longevity.Api.PrivateProfile;
using Longevity.Application.PrivateProfile;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Longevity.Infrastructure.Tests.PrivateProfile;

public sealed class PrivateProfileSecurityReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Private_policy_fails_closed_and_requires_one_valid_subject()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddPrivateProfileApi()
            .BuildServiceProvider();

        var authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal(PrivateProfileAuthorization.RejectAllScheme, authentication.DefaultScheme);

        var context = new DefaultHttpContext { RequestServices = provider };
        await provider.GetRequiredService<IAuthenticationService>().ChallengeAsync(context, null, null);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);

        var authorization = provider.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(Principal(new Claim("sub", "fictional-subject-a")), null, PrivateProfileAuthorization.PolicyName)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principal(new Claim(ClaimTypes.NameIdentifier, "fictional-subject-a")), null, PrivateProfileAuthorization.PolicyName)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal(), null, PrivateProfileAuthorization.PolicyName)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal(new Claim("sub", " fictional-subject-a")), null, PrivateProfileAuthorization.PolicyName)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal(
            new Claim("sub", "fictional-subject-a"),
            new Claim("sub", "fictional-subject-b")), null, PrivateProfileAuthorization.PolicyName)).Succeeded);
    }

    [Fact]
    public void Private_profile_registration_preserves_an_explicit_identity_provider_default()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("FictionalExternalIdentityProvider");
        services.AddPrivateProfileApi();

        using var provider = services.BuildServiceProvider();
        var authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal("FictionalExternalIdentityProvider", authentication.DefaultScheme);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" fictional-subject")]
    [InlineData("fictional-subject ")]
    [InlineData("fictional\nsubject")]
    public void Subject_validation_rejects_ambiguous_or_unsafe_values(string value)
    {
        Assert.False(PrivateProfileSubject.TryValidate(value, out _));
        Assert.Throws<PrivateProfileAuthorizationException>(() => PrivateProfileSubject.Require(value));
    }

    [Fact]
    public void Subject_validation_rejects_oversized_values()
    {
        var value = new string('x', PrivateProfileSubject.MaximumLength + 1);
        Assert.False(PrivateProfileSubject.TryValidate(value, out _));
    }

    [Fact]
    public async Task Structural_input_validation_matches_database_boundaries()
    {
        var service = Service();

        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.UpdateProfileAsync(
            new(null, 2027, null, null, PrivateProfileValues.Metric, "UTC"), CancellationToken.None));
        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.UpdateProfileAsync(
            new(null, 1980, null, null, PrivateProfileValues.Metric, "Mars/Olympus_Mons"), CancellationToken.None));
        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.RecordConsentAsync(
            new(PrivateProfileValues.ResearchUse, "foundation-v1", PrivateProfileValues.Declined, PrivateProfileValues.SystemDefaultCollectionSource),
            CancellationToken.None));
        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.AddMeasurementAsync(
            new(PrivateProfileValues.Weight, 70m, "cm", Now, PrivateProfileValues.SelfReported, null), CancellationToken.None));
        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.AddMeasurementAsync(
            new(PrivateProfileValues.Weight, 1_000_000_000_000m, "kg", Now, PrivateProfileValues.SelfReported, null), CancellationToken.None));
        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.AddMeasurementAsync(
            new(PrivateProfileValues.Weight, 70m, "kg", Now.AddMinutes(6), PrivateProfileValues.SelfReported, null), CancellationToken.None));
        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.AddLabAsync(
            new("Fictional marker", null, null, 100_000_000_000_000m, null, "mg/L", null, null, null, null, Now, PrivateProfileValues.LabReport, null),
            CancellationToken.None));
        await Assert.ThrowsAsync<PrivateProfileValidationException>(() => service.AddLabAsync(
            new("Fictional marker", null, null, 1m, null, "mg/L", null, null, "H", null, Now, PrivateProfileValues.LabReport, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Migration_and_store_enforce_backend_only_least_privilege_context()
    {
        var migration = await ReadRepositoryFileAsync("supabase", "migrations", "20260713000000_private_profile_foundation.sql");
        var store = await ReadRepositoryFileAsync("src", "Longevity.Infrastructure", "PrivateProfile", "PostgresPrivateProfileStore.cs");

        Assert.Contains("create role private_profile_api", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nologin nosuperuser nocreatedb nocreaterole noinherit nobypassrls", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("revoke all on schema private_profile from anon, authenticated, service_role", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alter default privileges revoke execute on functions from public", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter default privileges in schema private_profile revoke execute on functions", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alter default privileges in schema private_profile revoke all on sequences", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("for all to private_profile_api", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("for all to public", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request.jwt.claim.sub", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant usage on schema private_profile to authenticated", migration, StringComparison.OrdinalIgnoreCase);

        var roleActivation = store.IndexOf("set local role private_profile_api", StringComparison.OrdinalIgnoreCase);
        var subjectSetting = store.IndexOf("set_config('app.current_user_subject', @subject, true)", StringComparison.OrdinalIgnoreCase);
        Assert.True(roleActivation >= 0 && subjectSetting > roleActivation);
        Assert.Contains("not role.rolsuper", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not role.rolbypassrls", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not role.rolinherit", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current_user = session_user", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current_setting('app.current_user_subject', true)", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pg_has_role(session_user, 'private_profile_api', 'set')", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not boundary.rolcanlogin", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not boundary.rolbypassrls", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RollbackAsync(CancellationToken.None)", store, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Default_consent_initialization_has_database_and_store_idempotency_guards()
    {
        var migration = await ReadRepositoryFileAsync("supabase", "migrations", "20260713000000_private_profile_foundation.sql");
        var store = await ReadRepositoryFileAsync("src", "Longevity.Infrastructure", "PrivateProfile", "PostgresPrivateProfileStore.cs");

        Assert.Contains("create unique index private_consents_system_default_unique_idx", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("where collection_source = 'system_default'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("on conflict do nothing", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("from owned_profile p", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("for update", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default clock_timestamp()", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private_consents_system_default_check", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Catalog_validation_checks_roles_policies_grants_and_foreign_keys_without_private_rows()
    {
        var validation = await ReadRepositoryFileAsync("scripts", "validate-data", "validate_private_profile_schema.sql");

        Assert.Contains("not rolbypassrls", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("roles = array['private_profile_api']", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("information_schema.table_privileges", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private_consents_system_default_unique_idx", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("constraint_record.confdeltype = 'c'", validation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("from private_profile.profiles", validation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insert into private_profile", validation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Transactional_rls_validation_uses_fictional_two_subject_rows_and_rolls_back()
    {
        var validation = await ReadRepositoryFileAsync("scripts", "validate-data", "validate_private_profile_rls.sql");

        Assert.Contains("set local role private_profile_api", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("security-review-fictional-subject-a", validation, StringComparison.Ordinal);
        Assert.Contains("security-review-fictional-subject-b", validation, StringComparison.Ordinal);
        Assert.Contains("when insufficient_privilege then null", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("when unique_violation then null", validation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", validation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public.", validation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Production_authentication_does_not_accept_test_headers()
    {
        var currentUser = await ReadRepositoryFileAsync("src", "Longevity.Api", "PrivateProfile", "CurrentUserContext.cs");
        var authentication = await ReadRepositoryFileAsync("src", "Longevity.Api", "PrivateProfile", "PrivateProfileAuthentication.cs");

        Assert.DoesNotContain("X-Test-Subject", currentUser, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-Test-Subject", authentication, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AuthenticateResult.NoResult", authentication, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Safe_observability_omits_paths_subjects_bodies_and_exceptions()
    {
        var logger = new CapturingLogger<PrivateProfileObservabilityMiddleware>();
        var middleware = new PrivateProfileObservabilityMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/me/labs";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("fictional-subject-a private lab 987.65"));
        context.Request.ContentLength = context.Request.Body.Length;

        await middleware.InvokeAsync(context);

        var message = Assert.Single(logger.Messages);
        Assert.Contains("POST", message, StringComparison.Ordinal);
        Assert.Contains("204", message, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/me", message, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional-subject", message, StringComparison.Ordinal);
        Assert.DoesNotContain("987.65", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_private_request_is_rejected_before_endpoint_execution()
    {
        var invoked = false;
        var middleware = new PrivateProfileObservabilityMiddleware(
            _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            new CapturingLogger<PrivateProfileObservabilityMiddleware>());
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/me/profile";
        context.Request.ContentLength = PrivateProfileAuthorization.MaximumRequestBodyBytes + 1;

        await middleware.InvokeAsync(context);

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "fictional-test"));

    private static PrivateProfileService Service() =>
        new(new StoreThatMustNotBeCalled(), new CurrentUser(true, "fictional-subject-a"), new FixedTimeProvider(Now));

    private static async Task<string> ReadRepositoryFileAsync(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return await File.ReadAllTextAsync(Path.Combine([root, .. segments]));
    }

    private sealed record CurrentUser(bool IsAuthenticated, string? SubjectId) : ICurrentUserContext;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }

    private sealed class StoreThatMustNotBeCalled : IPrivateProfileStore
    {
        public Task<ProfileRecord> GetOrCreateProfileAsync(string externalSubjectId, CancellationToken cancellationToken) => Fail<ProfileRecord>();
        public Task<ProfileRecord> UpdateProfileAsync(string externalSubjectId, ProfileUpdateData update, CancellationToken cancellationToken) => Fail<ProfileRecord>();
        public Task<IReadOnlyList<ConsentRecord>> ListConsentsAsync(string externalSubjectId, CancellationToken cancellationToken) => Fail<IReadOnlyList<ConsentRecord>>();
        public Task<ConsentRecord> AppendConsentAsync(string externalSubjectId, RecordConsentRequest request, DateTimeOffset? grantedAt, DateTimeOffset? withdrawnAt, CancellationToken cancellationToken) => Fail<ConsentRecord>();
        public Task<CursorPage<BodyMeasurementRecord>> ListMeasurementsAsync(string externalSubjectId, ObservationCursor? cursor, int limit, CancellationToken cancellationToken) => Fail<CursorPage<BodyMeasurementRecord>>();
        public Task<BodyMeasurementRecord> AddMeasurementAsync(string externalSubjectId, BodyMeasurementRequest request, CancellationToken cancellationToken) => Fail<BodyMeasurementRecord>();
        public Task<CursorPage<LabObservationRecord>> ListLabsAsync(string externalSubjectId, ObservationCursor? cursor, int limit, CancellationToken cancellationToken) => Fail<CursorPage<LabObservationRecord>>();
        public Task<LabObservationRecord> AddLabAsync(string externalSubjectId, LabObservationRequest request, CancellationToken cancellationToken) => Fail<LabObservationRecord>();
        public Task<PreferenceRecord?> GetPreferencesAsync(string externalSubjectId, CancellationToken cancellationToken) => Fail<PreferenceRecord?>();
        public Task<PreferenceRecord> UpsertPreferencesAsync(string externalSubjectId, PreferencesRequest request, CancellationToken cancellationToken) => Fail<PreferenceRecord>();
        public Task<IReadOnlyList<GoalRecord>> ListGoalsAsync(string externalSubjectId, CancellationToken cancellationToken) => Fail<IReadOnlyList<GoalRecord>>();
        public Task<GoalRecord> AddGoalAsync(string externalSubjectId, CreateGoalRequest request, CancellationToken cancellationToken) => Fail<GoalRecord>();
        public Task<GoalRecord?> UpdateGoalAsync(string externalSubjectId, Guid goalId, GoalUpdateData update, CancellationToken cancellationToken) => Fail<GoalRecord?>();

        private static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("Store should not be called."));
    }
}
