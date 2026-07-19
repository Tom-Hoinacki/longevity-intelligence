using Longevity.Application.PrivateProfile;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Longevity.Infrastructure.PrivateProfile;

public sealed class PostgresPrivateProfileStore(
    NpgsqlDataSource dataSource,
    ILogger<PostgresPrivateProfileStore> logger) : IPrivateProfileStore
{
    private const string DefaultPolicyVersion = "foundation-v1";

    public Task<ProfileRecord> GetOrCreateProfileAsync(string externalSubjectId, CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await EnsureProfileAsync(connection, transaction, externalSubjectId, cancellationToken).ConfigureAwait(false);
            await EnsureDefaultConsentsAsync(connection, transaction, externalSubjectId, cancellationToken).ConfigureAwait(false);
            return await ReadProfileAsync(connection, transaction, externalSubjectId, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task<ProfileRecord> UpdateProfileAsync(
        string externalSubjectId,
        ProfileUpdateData update,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await EnsureProfileAsync(connection, transaction, externalSubjectId, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                update private_profile.profiles
                set birth_date = @birth_date,
                    birth_year = @birth_year,
                    sex_at_birth = @sex_at_birth,
                    gender = @gender,
                    preferred_measurement_system = @measurement_system,
                    time_zone = @time_zone,
                    updated_at = now()
                where external_subject_id = @external_subject_id
                returning profile_id, external_subject_id, birth_date, birth_year, sex_at_birth, gender,
                          preferred_measurement_system, time_zone, created_at, updated_at
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "birth_date", NpgsqlDbType.Date, update.BirthDate);
            Add(command, "birth_year", NpgsqlDbType.Integer, update.BirthYear);
            Add(command, "sex_at_birth", NpgsqlDbType.Text, update.SexAtBirth);
            Add(command, "gender", NpgsqlDbType.Text, update.Gender);
            Add(command, "measurement_system", NpgsqlDbType.Text, update.PreferredMeasurementSystem);
            Add(command, "time_zone", NpgsqlDbType.Text, update.TimeZone);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadProfileFromReaderAsync(reader, cancellationToken).ConfigureAwait(false)
                ?? throw new PrivateProfileNotFoundException();
        }, cancellationToken);

    public Task<IReadOnlyList<ConsentRecord>> ListConsentsAsync(string externalSubjectId, CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                select c.consent_id, c.profile_id, c.consent_type, c.policy_version, c.status,
                       c.granted_at, c.withdrawn_at, c.collection_source, c.created_at
                from private_profile.consents c
                join private_profile.profiles p on p.profile_id = c.profile_id
                where p.external_subject_id = @external_subject_id
                order by c.created_at desc, c.consent_id desc
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            var records = new List<ConsentRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                records.Add(new(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    NullableTimestamp(reader, 5), NullableTimestamp(reader, 6), reader.GetString(7), Timestamp(reader, 8)));
            }
            return (IReadOnlyList<ConsentRecord>)records;
        }, cancellationToken);

    public Task<ConsentRecord> AppendConsentAsync(
        string externalSubjectId,
        RecordConsentRequest request,
        DateTimeOffset? grantedAt,
        DateTimeOffset? withdrawnAt,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await EnsureProfileAsync(connection, transaction, externalSubjectId, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                with owned_profile as (
                    select p.profile_id
                    from private_profile.profiles p
                    where p.external_subject_id = @external_subject_id
                    for update
                )
                insert into private_profile.consents
                    (profile_id, consent_type, policy_version, status, granted_at, withdrawn_at, collection_source)
                select p.profile_id, @consent_type, @policy_version, @status, @granted_at, @withdrawn_at, @collection_source
                from owned_profile p
                returning consent_id, profile_id, consent_type, policy_version, status,
                          granted_at, withdrawn_at, collection_source, created_at
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "consent_type", NpgsqlDbType.Text, request.ConsentType);
            Add(command, "policy_version", NpgsqlDbType.Text, request.PolicyVersion);
            Add(command, "status", NpgsqlDbType.Text, request.Status);
            Add(command, "granted_at", NpgsqlDbType.TimestampTz, grantedAt);
            Add(command, "withdrawn_at", NpgsqlDbType.TimestampTz, withdrawnAt);
            Add(command, "collection_source", NpgsqlDbType.Text, request.CollectionSource);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadConsentFromReaderAsync(reader, cancellationToken).ConfigureAwait(false)
                ?? throw new PrivateProfileNotFoundException();
        }, cancellationToken);

    public Task<CursorPage<BodyMeasurementRecord>> ListMeasurementsAsync(
        string externalSubjectId,
        ObservationCursor? cursor,
        int limit,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                select m.observation_id, m.profile_id, m.measurement_type, m.numeric_value, m.unit,
                       m.observed_at, m.source_type, m.source_label, m.created_at
                from private_profile.body_measurements m
                join private_profile.profiles p on p.profile_id = m.profile_id
                where p.external_subject_id = @external_subject_id
                  and (
                      @cursor_at is null
                      or (m.observed_at, m.observation_id) < (@cursor_at, @cursor_id)
                  )
                order by m.observed_at desc, m.observation_id desc
                limit @limit
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "cursor_at", NpgsqlDbType.TimestampTz, cursor?.ObservedAt);
            Add(command, "cursor_id", NpgsqlDbType.Uuid, cursor?.ObservationId);
            Add(command, "limit", NpgsqlDbType.Integer, limit + 1);
            var records = new List<BodyMeasurementRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                records.Add(new(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3), reader.GetString(4),
                    Timestamp(reader, 5), reader.GetString(6), NullableString(reader, 7), Timestamp(reader, 8)));
            }
            var next = records.Count > limit ? new ObservationCursor(records[limit - 1].ObservedAt, records[limit - 1].ObservationId) : null;
            if (records.Count > limit) records.RemoveAt(limit);
            return new CursorPage<BodyMeasurementRecord>(records, next);
        }, cancellationToken);

    public Task<BodyMeasurementRecord> AddMeasurementAsync(
        string externalSubjectId,
        BodyMeasurementRequest request,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                insert into private_profile.body_measurements
                    (profile_id, measurement_type, numeric_value, unit, observed_at, source_type, source_label)
                select p.profile_id, @measurement_type, @numeric_value, @unit, @observed_at, @source_type, @source_label
                from private_profile.profiles p
                where p.external_subject_id = @external_subject_id
                returning observation_id, profile_id, measurement_type, numeric_value, unit,
                          observed_at, source_type, source_label, created_at
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "measurement_type", NpgsqlDbType.Text, request.MeasurementType);
            Add(command, "numeric_value", NpgsqlDbType.Numeric, request.NumericValue);
            Add(command, "unit", NpgsqlDbType.Text, request.Unit);
            Add(command, "observed_at", NpgsqlDbType.TimestampTz, request.ObservedAt);
            Add(command, "source_type", NpgsqlDbType.Text, request.SourceType);
            Add(command, "source_label", NpgsqlDbType.Text, request.SourceLabel);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadMeasurementFromReaderAsync(reader, cancellationToken).ConfigureAwait(false)
                ?? throw new PrivateProfileNotFoundException();
        }, cancellationToken);

    public Task<CursorPage<LabObservationRecord>> ListLabsAsync(
        string externalSubjectId,
        ObservationCursor? cursor,
        int limit,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                select l.observation_id, l.profile_id, l.test_name, l.standardized_code_system,
                       l.standardized_test_code, l.numeric_value, l.text_value, l.unit,
                       l.reference_range_minimum, l.reference_range_maximum, l.abnormal_flag,
                       l.specimen_or_panel_label, l.observed_at, l.source_type, l.source_label, l.created_at
                from private_profile.lab_observations l
                join private_profile.profiles p on p.profile_id = l.profile_id
                where p.external_subject_id = @external_subject_id
                  and (
                      @cursor_at is null
                      or (l.observed_at, l.observation_id) < (@cursor_at, @cursor_id)
                  )
                order by l.observed_at desc, l.observation_id desc
                limit @limit
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "cursor_at", NpgsqlDbType.TimestampTz, cursor?.ObservedAt);
            Add(command, "cursor_id", NpgsqlDbType.Uuid, cursor?.ObservationId);
            Add(command, "limit", NpgsqlDbType.Integer, limit + 1);
            var records = new List<LabObservationRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                records.Add(new(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), NullableString(reader, 3),
                    NullableString(reader, 4), NullableDecimal(reader, 5), NullableString(reader, 6), NullableString(reader, 7),
                    NullableDecimal(reader, 8), NullableDecimal(reader, 9), NullableString(reader, 10), NullableString(reader, 11),
                    Timestamp(reader, 12), reader.GetString(13), NullableString(reader, 14), Timestamp(reader, 15)));
            }
            var next = records.Count > limit ? new ObservationCursor(records[limit - 1].ObservedAt, records[limit - 1].ObservationId) : null;
            if (records.Count > limit) records.RemoveAt(limit);
            return new CursorPage<LabObservationRecord>(records, next);
        }, cancellationToken);

    public Task<LabObservationRecord> AddLabAsync(
        string externalSubjectId,
        LabObservationRequest request,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                insert into private_profile.lab_observations
                    (profile_id, test_name, standardized_code_system, standardized_test_code, numeric_value,
                     text_value, unit, reference_range_minimum, reference_range_maximum, abnormal_flag,
                     specimen_or_panel_label, observed_at, source_type, source_label)
                select p.profile_id, @test_name, @code_system, @test_code, @numeric_value,
                       @text_value, @unit, @reference_minimum, @reference_maximum, @abnormal_flag,
                       @specimen_or_panel_label, @observed_at, @source_type, @source_label
                from private_profile.profiles p
                where p.external_subject_id = @external_subject_id
                returning observation_id, profile_id, test_name, standardized_code_system,
                          standardized_test_code, numeric_value, text_value, unit,
                          reference_range_minimum, reference_range_maximum, abnormal_flag,
                          specimen_or_panel_label, observed_at, source_type, source_label, created_at
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "test_name", NpgsqlDbType.Text, request.TestName);
            Add(command, "code_system", NpgsqlDbType.Text, request.StandardizedCodeSystem);
            Add(command, "test_code", NpgsqlDbType.Text, request.StandardizedTestCode);
            Add(command, "numeric_value", NpgsqlDbType.Numeric, request.NumericValue);
            Add(command, "text_value", NpgsqlDbType.Text, request.TextValue);
            Add(command, "unit", NpgsqlDbType.Text, request.Unit);
            Add(command, "reference_minimum", NpgsqlDbType.Numeric, request.ReferenceRangeMinimum);
            Add(command, "reference_maximum", NpgsqlDbType.Numeric, request.ReferenceRangeMaximum);
            Add(command, "abnormal_flag", NpgsqlDbType.Text, request.AbnormalFlag);
            Add(command, "specimen_or_panel_label", NpgsqlDbType.Text, request.SpecimenOrPanelLabel);
            Add(command, "observed_at", NpgsqlDbType.TimestampTz, request.ObservedAt);
            Add(command, "source_type", NpgsqlDbType.Text, request.SourceType);
            Add(command, "source_label", NpgsqlDbType.Text, request.SourceLabel);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadLabFromReaderAsync(reader, cancellationToken).ConfigureAwait(false)
                ?? throw new PrivateProfileNotFoundException();
        }, cancellationToken);

    public Task<PreferenceRecord?> GetPreferencesAsync(string externalSubjectId, CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                select preferences.profile_id, monthly_longevity_budget, preferred_currency,
                       risk_tolerance, evidence_confidence_preference, cost_versus_confidence_preference,
                       insurance_use_preference, updated_at
                from private_profile.preferences
                join private_profile.profiles on profiles.profile_id = preferences.profile_id
                where profiles.external_subject_id = @external_subject_id
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            return new PreferenceRecord(
                reader.GetGuid(0), reader.GetDecimal(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), NullableString(reader, 6), Timestamp(reader, 7));
        }, cancellationToken);

    public Task<PreferenceRecord> UpsertPreferencesAsync(
        string externalSubjectId,
        PreferencesRequest request,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await EnsureProfileAsync(connection, transaction, externalSubjectId, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                insert into private_profile.preferences
                    (profile_id, monthly_longevity_budget, preferred_currency, risk_tolerance,
                     evidence_confidence_preference, cost_versus_confidence_preference, insurance_use_preference)
                select p.profile_id, @budget, @currency, @risk, @evidence, @cost, @insurance
                from private_profile.profiles p
                where p.external_subject_id = @external_subject_id
                on conflict (profile_id) do update set
                    monthly_longevity_budget = excluded.monthly_longevity_budget,
                    preferred_currency = excluded.preferred_currency,
                    risk_tolerance = excluded.risk_tolerance,
                    evidence_confidence_preference = excluded.evidence_confidence_preference,
                    cost_versus_confidence_preference = excluded.cost_versus_confidence_preference,
                    insurance_use_preference = excluded.insurance_use_preference,
                    updated_at = now()
                returning profile_id, monthly_longevity_budget, preferred_currency, risk_tolerance,
                          evidence_confidence_preference, cost_versus_confidence_preference,
                          insurance_use_preference, updated_at
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "budget", NpgsqlDbType.Numeric, request.MonthlyLongevityBudget);
            Add(command, "currency", NpgsqlDbType.Text, request.PreferredCurrency);
            Add(command, "risk", NpgsqlDbType.Text, request.RiskTolerance);
            Add(command, "evidence", NpgsqlDbType.Text, request.EvidenceConfidencePreference);
            Add(command, "cost", NpgsqlDbType.Text, request.CostVersusConfidencePreference);
            Add(command, "insurance", NpgsqlDbType.Text, request.InsuranceUsePreference);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new PrivateProfileNotFoundException();
            return new PreferenceRecord(
                reader.GetGuid(0), reader.GetDecimal(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), NullableString(reader, 6), Timestamp(reader, 7));
        }, cancellationToken);

    public Task<IReadOnlyList<GoalRecord>> ListGoalsAsync(string externalSubjectId, CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                select g.goal_id, g.profile_id, g.goal_type, g.user_label, g.priority, g.active, g.created_at, g.updated_at
                from private_profile.goals g
                join private_profile.profiles p on p.profile_id = g.profile_id
                where p.external_subject_id = @external_subject_id
                order by g.active desc, g.priority asc, g.created_at asc, g.goal_id asc
                limit 101
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            var records = new List<GoalRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (records.Count == 100) break;
                records.Add(new(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), NullableString(reader, 3),
                    reader.GetInt32(4), reader.GetBoolean(5), Timestamp(reader, 6), Timestamp(reader, 7)));
            }
            return (IReadOnlyList<GoalRecord>)records;
        }, cancellationToken);

    public Task<GoalRecord> AddGoalAsync(
        string externalSubjectId,
        CreateGoalRequest request,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await EnsureProfileAsync(connection, transaction, externalSubjectId, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                insert into private_profile.goals (profile_id, goal_type, user_label, priority, active)
                select p.profile_id, @goal_type, @user_label, @priority, @active
                from private_profile.profiles p
                where p.external_subject_id = @external_subject_id
                returning goal_id, profile_id, goal_type, user_label, priority, active, created_at, updated_at
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "goal_type", NpgsqlDbType.Text, request.GoalType);
            Add(command, "user_label", NpgsqlDbType.Text, request.UserLabel);
            Add(command, "priority", NpgsqlDbType.Integer, request.Priority);
            Add(command, "active", NpgsqlDbType.Boolean, request.Active);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadGoalFromReaderAsync(reader, cancellationToken).ConfigureAwait(false)
                ?? throw new PrivateProfileNotFoundException();
        }, cancellationToken);

    public Task<GoalRecord?> UpdateGoalAsync(
        string externalSubjectId,
        Guid goalId,
        GoalUpdateData update,
        CancellationToken cancellationToken) =>
        WithSubjectAsync(externalSubjectId, async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                update private_profile.goals g
                set priority = coalesce(@priority, g.priority),
                    active = coalesce(@active, g.active),
                    user_label = coalesce(@user_label, g.user_label),
                    updated_at = now()
                from private_profile.profiles p
                where g.goal_id = @goal_id
                  and p.profile_id = g.profile_id
                  and p.external_subject_id = @external_subject_id
                returning g.goal_id, g.profile_id, g.goal_type, g.user_label, g.priority, g.active, g.created_at, g.updated_at
                """);
            Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
            Add(command, "goal_id", NpgsqlDbType.Uuid, goalId);
            Add(command, "priority", NpgsqlDbType.Integer, update.Priority);
            Add(command, "active", NpgsqlDbType.Boolean, update.Active);
            Add(command, "user_label", NpgsqlDbType.Text, update.UserLabel);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadGoalFromReaderAsync(reader, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    private async Task<T> WithSubjectAsync<T>(
        string externalSubjectId,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var subject = PrivateProfileSubject.Require(externalSubjectId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ConfigureSecurityContextAsync(connection, transaction, subject, cancellationToken).ConfigureAwait(false);
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the operation failure. Disposal provides a second rollback boundary.
                logger.LogWarning("Private-profile transaction rollback did not complete cleanly; connection disposal will enforce cleanup.");
            }
            throw;
        }
    }

    private async Task ConfigureSecurityContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string subject,
        CancellationToken cancellationToken)
    {
        try
        {
            await using (var check = Command(connection, transaction, """
                select coalesce((
                    select not role.rolsuper
                       and not role.rolbypassrls
                       and not role.rolcreaterole
                       and not role.rolcreatedb
                       and not role.rolinherit
                       and current_user = session_user
                       and nullif(current_setting('app.current_user_subject', true), '') is null
                       and role.rolname <> 'private_profile_api'
                       and pg_has_role(session_user, 'private_profile_api', 'set')
                       and exists (
                           select 1
                           from pg_roles boundary
                           where boundary.rolname = 'private_profile_api'
                             and not boundary.rolcanlogin
                             and not boundary.rolsuper
                             and not boundary.rolbypassrls
                             and not boundary.rolcreaterole
                             and not boundary.rolcreatedb
                             and not boundary.rolinherit
                             and not exists (
                                 select 1
                                 from pg_namespace boundary_namespace
                                 where boundary_namespace.nspname = 'private_profile'
                                   and boundary_namespace.nspowner = boundary.oid
                             )
                             and not exists (
                                 select 1
                                 from pg_class boundary_relation
                                 join pg_namespace boundary_namespace
                                   on boundary_namespace.oid = boundary_relation.relnamespace
                                 where boundary_namespace.nspname = 'private_profile'
                                   and boundary_relation.relowner = boundary.oid
                             )
                       )
                       and not exists (
                           select 1
                           from pg_namespace namespace
                           where namespace.nspname = 'private_profile'
                             and namespace.nspowner = role.oid
                       )
                       and not exists (
                           select 1
                           from pg_class relation
                           join pg_namespace namespace on namespace.oid = relation.relnamespace
                           where namespace.nspname = 'private_profile'
                             and relation.relowner = role.oid
                       )
                    from pg_roles role
                    where role.rolname = session_user
                ), false)
                """))
            {
                var safelyConfigured = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (safelyConfigured is not true)
                {
                    logger.LogError("Private-profile database security-context validation failed.");
                    throw new PrivateProfileSecurityConfigurationException();
                }
            }

            await using (var activateRole = Command(connection, transaction, "set local role private_profile_api"))
            {
                await activateRole.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var setSubject = Command(connection, transaction,
                "select set_config('app.current_user_subject', @subject, true)"))
            {
                Add(setSubject, "subject", NpgsqlDbType.Text, subject);
                await setSubject.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (PostgresException)
        {
            logger.LogError("Private-profile database security-context activation failed.");
            throw new PrivateProfileSecurityConfigurationException();
        }
    }

    private static async Task EnsureProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string externalSubjectId,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            insert into private_profile.profiles (external_subject_id)
            values (@external_subject_id)
            on conflict (external_subject_id) do nothing
            """);
        Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureDefaultConsentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string externalSubjectId,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            insert into private_profile.consents
                (profile_id, consent_type, policy_version, status, collection_source)
            select p.profile_id, defaults.consent_type, @policy_version, 'declined', 'system_default'
            from private_profile.profiles p
            cross join (values
                ('profile_data_storage'),
                ('personalized_analysis'),
                ('research_use'),
                ('deidentified_aggregate_data_use'),
                ('commercial_partner_matching')
            ) as defaults(consent_type)
            where p.external_subject_id = @external_subject_id
              and not exists (
                  select 1 from private_profile.consents c
                  where c.profile_id = p.profile_id
                    and c.consent_type = defaults.consent_type
              )
            on conflict do nothing
            """);
        Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
        Add(command, "policy_version", NpgsqlDbType.Text, DefaultPolicyVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProfileRecord> ReadProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string externalSubjectId,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            select profile_id, external_subject_id, birth_date, birth_year, sex_at_birth, gender,
                   preferred_measurement_system, time_zone, created_at, updated_at
            from private_profile.profiles
            where external_subject_id = @external_subject_id
            """);
        Add(command, "external_subject_id", NpgsqlDbType.Text, externalSubjectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadProfileFromReaderAsync(reader, cancellationToken).ConfigureAwait(false)
            ?? throw new PrivateProfileNotFoundException();
    }

    private static async Task<ProfileRecord?> ReadProfileFromReaderAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(
            reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3), NullableString(reader, 4), NullableString(reader, 5),
            reader.GetString(6), reader.GetString(7), Timestamp(reader, 8), Timestamp(reader, 9));
    }

    private static async Task<ConsentRecord?> ReadConsentFromReaderAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            NullableTimestamp(reader, 5), NullableTimestamp(reader, 6), reader.GetString(7), Timestamp(reader, 8));
    }

    private static async Task<BodyMeasurementRecord?> ReadMeasurementFromReaderAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3), reader.GetString(4),
            Timestamp(reader, 5), reader.GetString(6), NullableString(reader, 7), Timestamp(reader, 8));
    }

    private static async Task<LabObservationRecord?> ReadLabFromReaderAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), NullableString(reader, 3), NullableString(reader, 4),
            NullableDecimal(reader, 5), NullableString(reader, 6), NullableString(reader, 7), NullableDecimal(reader, 8),
            NullableDecimal(reader, 9), NullableString(reader, 10), NullableString(reader, 11), Timestamp(reader, 12),
            reader.GetString(13), NullableString(reader, 14), Timestamp(reader, 15));
    }

    private static async Task<GoalRecord?> ReadGoalFromReaderAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), NullableString(reader, 3), reader.GetInt32(4),
            reader.GetBoolean(5), Timestamp(reader, 6), Timestamp(reader, 7));
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql) =>
        new(sql, connection, transaction);

    private static void Add<T>(NpgsqlCommand command, string name, NpgsqlDbType type, T? value) =>
        command.Parameters.AddWithValue(name, type, (object?)value ?? DBNull.Value);

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static decimal? NullableDecimal(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static DateTimeOffset? NullableTimestamp(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Timestamp(reader, ordinal);

    private static DateTimeOffset Timestamp(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

public sealed class DisabledPrivateProfileStore : IPrivateProfileStore
{
    public Task<ProfileRecord> GetOrCreateProfileAsync(string externalSubjectId, CancellationToken cancellationToken) => Unavailable<ProfileRecord>();
    public Task<ProfileRecord> UpdateProfileAsync(string externalSubjectId, ProfileUpdateData update, CancellationToken cancellationToken) => Unavailable<ProfileRecord>();
    public Task<IReadOnlyList<ConsentRecord>> ListConsentsAsync(string externalSubjectId, CancellationToken cancellationToken) => Unavailable<IReadOnlyList<ConsentRecord>>();
    public Task<ConsentRecord> AppendConsentAsync(string externalSubjectId, RecordConsentRequest request, DateTimeOffset? grantedAt, DateTimeOffset? withdrawnAt, CancellationToken cancellationToken) => Unavailable<ConsentRecord>();
    public Task<CursorPage<BodyMeasurementRecord>> ListMeasurementsAsync(string externalSubjectId, ObservationCursor? cursor, int limit, CancellationToken cancellationToken) => Unavailable<CursorPage<BodyMeasurementRecord>>();
    public Task<BodyMeasurementRecord> AddMeasurementAsync(string externalSubjectId, BodyMeasurementRequest request, CancellationToken cancellationToken) => Unavailable<BodyMeasurementRecord>();
    public Task<CursorPage<LabObservationRecord>> ListLabsAsync(string externalSubjectId, ObservationCursor? cursor, int limit, CancellationToken cancellationToken) => Unavailable<CursorPage<LabObservationRecord>>();
    public Task<LabObservationRecord> AddLabAsync(string externalSubjectId, LabObservationRequest request, CancellationToken cancellationToken) => Unavailable<LabObservationRecord>();
    public Task<PreferenceRecord?> GetPreferencesAsync(string externalSubjectId, CancellationToken cancellationToken) => Unavailable<PreferenceRecord?>();
    public Task<PreferenceRecord> UpsertPreferencesAsync(string externalSubjectId, PreferencesRequest request, CancellationToken cancellationToken) => Unavailable<PreferenceRecord>();
    public Task<IReadOnlyList<GoalRecord>> ListGoalsAsync(string externalSubjectId, CancellationToken cancellationToken) => Unavailable<IReadOnlyList<GoalRecord>>();
    public Task<GoalRecord> AddGoalAsync(string externalSubjectId, CreateGoalRequest request, CancellationToken cancellationToken) => Unavailable<GoalRecord>();
    public Task<GoalRecord?> UpdateGoalAsync(string externalSubjectId, Guid goalId, GoalUpdateData update, CancellationToken cancellationToken) => Unavailable<GoalRecord?>();

    private static Task<T> Unavailable<T>() => Task.FromException<T>(new PrivateProfilePersistenceUnavailableException());
}
