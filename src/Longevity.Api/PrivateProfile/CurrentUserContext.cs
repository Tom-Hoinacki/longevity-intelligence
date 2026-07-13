using System.Security.Claims;
using Longevity.Application.PrivateProfile;

namespace Longevity.Api.PrivateProfile;

public sealed class HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public string? SubjectId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return null;
            return user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
