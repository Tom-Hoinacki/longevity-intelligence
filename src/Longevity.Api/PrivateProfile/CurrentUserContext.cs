using System.Security.Claims;
using Longevity.Application.PrivateProfile;

namespace Longevity.Api.PrivateProfile;

public sealed class HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public string? SubjectId =>
        TryGetTrustedSubject(httpContextAccessor.HttpContext?.User, out var subject) ? subject : null;

    public static bool TryGetTrustedSubject(ClaimsPrincipal? user, out string subject)
    {
        subject = string.Empty;
        if (user?.Identity?.IsAuthenticated != true) return false;

        var subjectClaims = user.FindAll("sub").Select(claim => claim.Value).ToArray();
        string? value;
        if (subjectClaims.Length == 1)
        {
            value = subjectClaims[0];
        }
        else if (subjectClaims.Length == 0)
        {
            var nameIdentifierClaims = user.FindAll(ClaimTypes.NameIdentifier).Select(claim => claim.Value).ToArray();
            if (nameIdentifierClaims.Length != 1) return false;
            value = nameIdentifierClaims[0];
        }
        else
        {
            return false;
        }

        return PrivateProfileSubject.TryValidate(value, out subject);
    }
}
