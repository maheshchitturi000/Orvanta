using System.Security.Claims;

namespace Orvanta.Shared.Security;

public static class OrvantaClaims
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string SessionId = "sid";

    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(UserId) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id)
            ? id
            : throw new UnauthorizedAccessException("The access token has no valid subject.");
    }
}

public static class OrvantaRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
}
