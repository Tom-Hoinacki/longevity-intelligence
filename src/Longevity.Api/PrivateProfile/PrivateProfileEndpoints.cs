using Longevity.Application.PrivateProfile;
using Microsoft.AspNetCore.Mvc;

namespace Longevity.Api.PrivateProfile;

public static class PrivateProfileEndpoints
{
    public static WebApplication MapPrivateProfileApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/me")
            .RequireAuthorization(PrivateProfileAuthorization.PolicyName)
            .WithMetadata(new RequestSizeLimitAttribute(PrivateProfileAuthorization.MaximumRequestBodyBytes))
            .WithTags("Private Profile");

        group.MapGet("/profile", GetProfileAsync);
        group.MapPut("/profile", UpdateProfileAsync);
        group.MapGet("/consents", GetConsentsAsync);
        group.MapPost("/consents", PostConsentAsync);
        group.MapPost("/consents/{consentType}/withdraw", WithdrawConsentAsync);
        group.MapGet("/measurements", GetMeasurementsAsync);
        group.MapPost("/measurements", PostMeasurementAsync);
        group.MapGet("/labs", GetLabsAsync);
        group.MapPost("/labs", PostLabAsync);
        group.MapGet("/preferences", GetPreferencesAsync);
        group.MapPut("/preferences", PutPreferencesAsync);
        group.MapGet("/goals", GetGoalsAsync);
        group.MapPost("/goals", PostGoalAsync);
        group.MapPatch("/goals/{goalId:guid}", PatchGoalAsync);
        return app;
    }

    public static Task<IResult> GetProfileAsync(
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateProfileService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () => Results.Ok(await service.GetOrCreateProfileAsync(cancellationToken).ConfigureAwait(false)));

    public static Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest? request,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateProfileService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            if (request is null) return BadRequest("invalid_request", "Profile request is required.");
            return Results.Ok(await service.UpdateProfileAsync(request, cancellationToken).ConfigureAwait(false));
        });

    public static Task<IResult> GetConsentsAsync(
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateConsentService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () => Results.Ok(await service.ListConsentsAsync(cancellationToken).ConfigureAwait(false)));

    public static Task<IResult> PostConsentAsync(
        RecordConsentRequest? request,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateConsentService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            if (request is null) return BadRequest("invalid_request", "Consent request is required.");
            return Results.Created("/api/v1/me/consents", await service.RecordConsentAsync(request, cancellationToken).ConfigureAwait(false));
        });

    public static Task<IResult> WithdrawConsentAsync(
        string consentType,
        WithdrawConsentRequest? request,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateConsentService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            if (request is null) return BadRequest("invalid_request", "Consent withdrawal request is required.");
            return Results.Ok(await service.WithdrawConsentAsync(consentType, request, cancellationToken).ConfigureAwait(false));
        });

    public static Task<IResult> GetMeasurementsAsync(
        int? limit,
        string? cursor,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateObservationService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () => Results.Ok(await service.ListMeasurementsAsync(limit ?? 25, cursor, cancellationToken).ConfigureAwait(false)));

    public static Task<IResult> PostMeasurementAsync(
        BodyMeasurementRequest? request,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateObservationService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            if (request is null) return BadRequest("invalid_request", "Measurement request is required.");
            return Results.Created("/api/v1/me/measurements", await service.AddMeasurementAsync(request, cancellationToken).ConfigureAwait(false));
        });

    public static Task<IResult> GetLabsAsync(
        int? limit,
        string? cursor,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateObservationService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () => Results.Ok(await service.ListLabsAsync(limit ?? 25, cursor, cancellationToken).ConfigureAwait(false)));

    public static Task<IResult> PostLabAsync(
        LabObservationRequest? request,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateObservationService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            if (request is null) return BadRequest("invalid_request", "Lab request is required.");
            return Results.Created("/api/v1/me/labs", await service.AddLabAsync(request, cancellationToken).ConfigureAwait(false));
        });

    public static Task<IResult> GetPreferencesAsync(
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivatePreferenceService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            var response = await service.GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
            return response is null ? NotFound("preferences_not_found", "No preferences have been saved.") : Results.Ok(response);
        });

    public static Task<IResult> PutPreferencesAsync(
        PreferencesRequest? request,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivatePreferenceService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            if (request is null) return BadRequest("invalid_request", "Preferences request is required.");
            return Results.Ok(await service.UpsertPreferencesAsync(request, cancellationToken).ConfigureAwait(false));
        });

    public static Task<IResult> GetGoalsAsync(
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateGoalService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () => Results.Ok(await service.ListGoalsAsync(cancellationToken).ConfigureAwait(false)));

    public static Task<IResult> PostGoalAsync(
        CreateGoalRequest? request,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateGoalService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            if (request is null) return BadRequest("invalid_request", "Goal request is required.");
            return Results.Created("/api/v1/me/goals", await service.AddGoalAsync(request, cancellationToken).ConfigureAwait(false));
        });

    public static Task<IResult> PatchGoalAsync(
        Guid goalId,
        UpdateGoalRequest? request,
        [FromServices] ICurrentUserContext currentUser,
        [FromServices] IPrivateGoalService service,
        CancellationToken cancellationToken) =>
        ExecuteAsync(currentUser, async () =>
        {
            if (request is null) return BadRequest("invalid_request", "Goal request is required.");
            return Results.Ok(await service.UpdateGoalAsync(goalId, request, cancellationToken).ConfigureAwait(false));
        });

    private static async Task<IResult> ExecuteAsync(ICurrentUserContext currentUser, Func<Task<IResult>> action)
    {
        var authorization = CheckAuthorization(currentUser);
        if (authorization is not null) return authorization;

        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PrivateProfileValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(exception.Errors),
                statusCode: StatusCodes.Status400BadRequest,
                title: "The private-profile request is invalid.");
        }
        catch (PrivateProfilePersistenceUnavailableException)
        {
            return Problem("private_profile_unavailable", "Private-profile persistence is unavailable.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (PrivateProfileSecurityConfigurationException)
        {
            return Problem("private_profile_unavailable", "Private-profile database access is not safely configured.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (PrivateProfileNotFoundException)
        {
            return NotFound("private_profile_not_found", "The requested private-profile resource was not found.");
        }
        catch (PrivateProfileConflictException)
        {
            return Problem("private_profile_conflict", "The private-profile operation conflicts with current state.", StatusCodes.Status409Conflict);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (PrivateProfileAuthorizationException)
        {
            return Results.Forbid();
        }
        catch
        {
            return Problem("private_profile_unavailable", "Private-profile service is temporarily unavailable.", StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult? CheckAuthorization(ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        return PrivateProfileSubject.TryValidate(currentUser.SubjectId, out _) ? null : Results.Forbid();
    }

    private static IResult BadRequest(string code, string message) => Results.BadRequest(new PrivateProfileErrorResponse(code, message));

    private static IResult NotFound(string code, string message) => Results.NotFound(new PrivateProfileErrorResponse(code, message));

    private static IResult Problem(string code, string message, int statusCode) =>
        Results.Json(new PrivateProfileErrorResponse(code, message), statusCode: statusCode);
}

public sealed record PrivateProfileErrorResponse(string Code, string Message);
