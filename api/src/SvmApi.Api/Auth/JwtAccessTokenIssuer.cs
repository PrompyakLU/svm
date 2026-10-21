using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using SvmApi.Application.Auth;

namespace SvmApi.Api.Auth;

/// <summary>
/// <see cref="IAccessTokenIssuer"/> (US-001): JWT HS256 อายุสั้น. claims: sub, sid, jti, iat (+ iss/aud/nbf/exp).
/// ไม่ใส่ roles/permissions/scopes ใน token — โหลดจาก DB ทุก request (docs/authentication-server.md).
/// </summary>
public sealed class JwtAccessTokenIssuer(
    IOptions<JwtOptions> jwtOptions,
    IOptions<AuthOptions> authOptions,
    TimeProvider clock) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();
    private readonly Lazy<SigningCredentials> _credentials = new(() =>
        new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Value.SigningKey)),
            SecurityAlgorithms.HmacSha256));

    public AccessToken Issue(Guid sessionId, AuthUserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var jwt = jwtOptions.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(authOptions.Value.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials.Value,
            Claims = new Dictionary<string, object>
            {
                [JwtAuthClaims.Subject] = user.Id.ToString(),
                [JwtAuthClaims.SessionId] = sessionId.ToString(),
                [JwtAuthClaims.TokenId] = Guid.NewGuid().ToString(),
            },
        };

        return new AccessToken(Handler.CreateToken(descriptor), expires);
    }
}

/// <summary>ชื่อ claim ใน access token.</summary>
public static class JwtAuthClaims
{
    public const string Subject = "sub";
    public const string SessionId = "sid";
    public const string TokenId = "jti";
}
