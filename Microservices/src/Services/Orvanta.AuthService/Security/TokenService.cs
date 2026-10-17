using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orvanta.AuthService.Domain;
using Orvanta.Shared.Security;

namespace Orvanta.AuthService.Security;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user, Guid sessionId);
}

/// <summary>
/// Issues signed (HS256) and encrypted (JWE: A256KW + A256CBC-HS512) access
/// tokens. Encryption keeps claims such as email unreadable to anyone who
/// intercepts or inspects the token; only Orvanta services hold the key.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user, Guid sessionId)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(OrvantaClaims.UserId, user.Id.ToString()),
                new Claim(OrvantaClaims.Email, user.Email),
                new Claim(OrvantaClaims.Name, user.FullName),
                new Claim(OrvantaClaims.Role, user.Role),
                new Claim(OrvantaClaims.SessionId, sessionId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            SigningCredentials = JwtKeys.SigningCredentials(_options),
            EncryptingCredentials = JwtKeys.EncryptingCredentials(_options)
        };

        return (_handler.CreateToken(descriptor), expires);
    }
}
