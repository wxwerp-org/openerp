using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace OpenERP.Api.Authentication;

internal sealed class AccessTokens
{
    private const string Issuer = "OpenERP";
    private const string Audience = "OpenERP.Api";
    private const string SigningAlgorithm = SecurityAlgorithms.RsaSha256;
    internal const string LoginIdClaim = "sid";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _signingCredentials;
    private readonly RsaSecurityKey _validationKey;

    internal AccessTokens()
    {
        using var rsa = RSA.Create(2048);
        _signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa.ExportParameters(true)), SigningAlgorithm);
        _validationKey = new RsaSecurityKey(rsa.ExportParameters(false));
    }

    internal TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = _validationKey,
        ValidAlgorithms = [SigningAlgorithm],
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    internal (string Value, DateTimeOffset ExpiresAt)? Issue(AdminRefreshToken login, DateTimeOffset now)
    {
        var deadline = now.Add(Lifetime);
        if (login.ExpiresAt < deadline)
            deadline = login.ExpiresAt;

        // JWT 的 NumericDate 使用秒精度，接口返回的到期时间与令牌一致。
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(deadline.ToUnixTimeSeconds());
        if (expiresAt.ToUnixTimeSeconds() <= now.ToUnixTimeSeconds())
            return null;

        var value = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, AdminAccount.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(LoginIdClaim, login.Id.ToString("D"))
            ])
        });
        return (value, expiresAt);
    }
}
