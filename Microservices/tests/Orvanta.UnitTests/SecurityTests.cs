using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orvanta.AuthService.Domain;
using Orvanta.AuthService.Security;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.UnitTests;

public class SecurityTests
{
    private static string Key(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    [Fact]
    public void PasswordHasher_never_stores_plain_text_and_verifies()
    {
        var hasher = new PasswordHasher("pepper");
        var hash = hasher.Hash("Str0ng!Pass");

        Assert.DoesNotContain("Str0ng!Pass", hash);
        Assert.StartsWith("pbkdf2-sha256$600000$", hash);
        Assert.Equal(PasswordVerification.Success, hasher.Verify("Str0ng!Pass", hash));
        Assert.Equal(PasswordVerification.Failed, hasher.Verify("wrong", hash));
    }

    [Fact]
    public void PasswordHasher_uses_unique_salt_and_pepper()
    {
        var hasher = new PasswordHasher("pepper");
        Assert.NotEqual(hasher.Hash("same"), hasher.Hash("same"));

        var otherPepper = new PasswordHasher("different");
        Assert.Equal(PasswordVerification.Failed, otherPepper.Verify("same", hasher.Hash("same")));
    }

    [Fact]
    public void AesGcmProtector_round_trips_and_detects_tampering()
    {
        var protector = new AesGcmProtector(Key(32));
        var secret = protector.Protect("provider-access-token");

        Assert.NotEqual("provider-access-token", secret);
        Assert.Equal("provider-access-token", protector.Unprotect(secret));

        var bytes = Convert.FromBase64String(secret);
        bytes[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public async Task Access_token_is_encrypted_and_validates_with_shared_keys()
    {
        var options = new JwtOptions { SigningKey = Key(64), EncryptionKey = Key(32), AccessTokenMinutes = 5 };
        var user = new User { FullName = "Asha Rao", Email = "asha@example.com", PhoneNumber = "9876543210" };

        var (token, _) = new TokenService(Options.Create(options)).CreateAccessToken(user, Guid.NewGuid());

        // JWE compact serialization has 5 parts; a plain JWS has 3.
        Assert.Equal(5, token.Split('.').Length);
        Assert.DoesNotContain("asha", token, StringComparison.OrdinalIgnoreCase);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = JwtKeys.SigningKey(options),
            TokenDecryptionKey = JwtKeys.EncryptionKey(options)
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(user.Id.ToString(), result.Claims[OrvantaClaims.UserId]);
        Assert.Equal("asha@example.com", result.Claims[OrvantaClaims.Email]);
    }

    [Fact]
    public async Task Access_token_is_rejected_with_wrong_decryption_key()
    {
        var options = new JwtOptions { SigningKey = Key(64), EncryptionKey = Key(32) };
        var (token, _) = new TokenService(Options.Create(options)).CreateAccessToken(new User { Email = "a@b.c" }, Guid.NewGuid());

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = JwtKeys.SigningKey(options),
            TokenDecryptionKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ServiceResult_maps_to_ResponseCls()
    {
        var ok = ServiceResult<string>.Created("value", "made").ToResponse();
        Assert.Equal(201, ok.StatusCode);
        Assert.Equal("made", ok.StatusMessage);
        Assert.Equal("value", ok.StatusObject);

        var fail = ServiceResult<string>.Fail(409, "exists").ToResponse();
        Assert.Equal(409, fail.StatusCode);
        Assert.Null(fail.StatusObject);
    }
}
