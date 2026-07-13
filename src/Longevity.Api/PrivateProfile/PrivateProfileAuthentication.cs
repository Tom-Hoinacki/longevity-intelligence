using System.Text.Encodings.Web;
using Longevity.Application.PrivateProfile;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Longevity.Api.PrivateProfile;

public static class PrivateProfileAuthorization
{
    public const string PolicyName = "PrivateProfileOwner";
    public const string RejectAllScheme = "PrivateProfileRejectAll";
    public const long MaximumRequestBodyBytes = 64 * 1024;
}

internal sealed class RejectAllPrivateProfileAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
