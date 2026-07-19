using System.Security.Cryptography;
using System.Text;

namespace Longevity.Api.Security;

public static class TrustedBearerAuthorization
{
    public static bool IsAuthorized(HttpRequest request, string? expectedSecret)
    {
        if (string.IsNullOrWhiteSpace(expectedSecret) || !request.Headers.TryGetValue("Authorization", out var header)) return false;
        const string prefix = "Bearer ";
        var supplied = header.ToString();
        if (!supplied.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(expectedSecret));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(supplied[prefix.Length..].Trim()));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
