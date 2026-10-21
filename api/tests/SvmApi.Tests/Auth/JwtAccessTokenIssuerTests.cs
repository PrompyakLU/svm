using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;

namespace SvmApi.Tests.Auth;

/// <summary>JWT issuing (US-001): claims, expiry, signature; no roles/permissions/scopes inside the token.</summary>
public class JwtAccessTokenIssuerTests
{
    private const string Key = "unit-test-signing-key-0123456789-ABCDEF";

    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly AuthUserDto User = new(
        Guid.NewGuid(), "officer1", "Officer One",
        ["VulnMgmtOfficer"], ["asset.read"], [new AuthScope("Organization", "*")]);

    [Fact]
    public void Issue_sets_subject_session_and_unique_token_id()
    {
        var sessionId = Guid.NewGuid();
        var issuer = NewIssuer();

        var first = new JsonWebToken(issuer.Issue(sessionId, User).Value);
        var second = new JsonWebToken(issuer.Issue(sessionId, User).Value);

        Assert.Equal(User.Id.ToString(), first.Subject);
        Assert.Equal(sessionId.ToString(), first.GetPayloadValue<string>(JwtAuthClaims.SessionId));
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Issue_sets_issuer_audience_and_lifetime_from_options()
    {
        var token = NewIssuer(accessTokenMinutes: 5).Issue(Guid.NewGuid(), User);
        var jwt = new JsonWebToken(token.Value);

        Assert.Equal("svm-test", jwt.Issuer);
        Assert.Contains("svm-test-web", jwt.Audiences);
        Assert.Equal(Now.AddMinutes(5), token.ExpiresAt);
        Assert.Equal(Now.AddMinutes(5).UtcDateTime, jwt.ValidTo);
        Assert.Equal(Now.UtcDateTime, jwt.ValidFrom);
        Assert.Equal(Now.UtcDateTime, jwt.IssuedAt);
    }

    [Fact]
    public void Issue_does_not_embed_roles_permissions_or_scopes()
    {
        var jwt = new JsonWebToken(NewIssuer().Issue(Guid.NewGuid(), User).Value);

        var claimTypes = jwt.Claims.Select(c => c.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var forbidden in new[] { "role", "roles", "permissions", "permission", "scope", "scopes", "username", "name" })
        {
            Assert.DoesNotContain(forbidden, claimTypes);
        }

        Assert.DoesNotContain(User.Username, Decode(jwt.EncodedPayload));
    }

    [Fact]
    public async Task Issued_token_validates_with_the_signing_key_and_rejects_a_different_key()
    {
        var token = NewIssuer().Issue(Guid.NewGuid(), User).Value;
        var handler = new JsonWebTokenHandler();

        var ok = await handler.ValidateTokenAsync(token, Parameters(Key));
        var wrongKey = await handler.ValidateTokenAsync(token, Parameters("a-completely-different-key-9876543210-ZYX"));

        Assert.True(ok.IsValid);
        Assert.False(wrongKey.IsValid);
    }

    [Fact]
    public async Task Issued_token_uses_hmac_sha256_and_is_rejected_after_expiry()
    {
        var token = NewIssuer(accessTokenMinutes: 5).Issue(Guid.NewGuid(), User).Value;
        var jwt = new JsonWebToken(token);
        var handler = new JsonWebTokenHandler();

        var lifetimeChecked = Parameters(Key);
        lifetimeChecked.ValidateLifetime = true;
        lifetimeChecked.LifetimeValidator = (notBefore, expires, _, _) =>
            notBefore <= Now.AddMinutes(6).UtcDateTime && expires > Now.AddMinutes(6).UtcDateTime;
        var afterExpiry = await handler.ValidateTokenAsync(token, lifetimeChecked);

        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Alg);
        Assert.False(afterExpiry.IsValid);
    }

    private static JwtAccessTokenIssuer NewIssuer(int accessTokenMinutes = 5)
        => new(
            Options.Create(new JwtOptions { Issuer = "svm-test", Audience = "svm-test-web", SigningKey = Key }),
            Options.Create(new AuthOptions { AccessTokenMinutes = accessTokenMinutes }),
            new FixedTimeProvider(Now));

    private static TokenValidationParameters Parameters(string key) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidIssuer = "svm-test",
        ValidateAudience = true,
        ValidAudience = "svm-test-web",
        ValidateLifetime = false,
    };

    private static string Decode(string base64Url)
        => Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(base64Url));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
