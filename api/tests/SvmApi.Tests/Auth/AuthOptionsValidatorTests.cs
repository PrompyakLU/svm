using SvmApi.Api.Auth;
using SvmApi.Application.Auth;

namespace SvmApi.Tests.Auth;

/// <summary>Startup validation fails closed on a missing/weak JWT signing key and never echoes the key.</summary>
public class AuthOptionsValidatorTests
{
    private const string StrongKey = "Zk3v9Qp7Lm2xR8tY5wB1nC6dF4hJ0sGa";

    [Fact]
    public void Valid_options_succeed()
        => Assert.True(Validate(new JwtOptions { SigningKey = StrongKey }).Succeeded);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_signing_key_fails(string key)
    {
        var result = Validate(new JwtOptions { SigningKey = key });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Auth__Jwt__SigningKey"));
    }

    [Fact]
    public void Short_signing_key_fails_without_echoing_the_value()
    {
        const string shortKey = "short-secret-value";

        var result = Validate(new JwtOptions { SigningKey = shortKey });

        Assert.True(result.Failed);
        Assert.DoesNotContain(result.Failures!, f => f.Contains(shortKey));
    }

    [Fact]
    public void Predictable_signing_key_fails()
        => Assert.True(Validate(new JwtOptions { SigningKey = new string('a', 64) }).Failed);

    [Fact]
    public void Missing_issuer_or_audience_fails()
    {
        Assert.True(Validate(new JwtOptions { SigningKey = StrongKey, Issuer = "" }).Failed);
        Assert.True(Validate(new JwtOptions { SigningKey = StrongKey, Audience = " " }).Failed);
    }

    [Theory]
    [InlineData(0, 30, 8)]
    [InlineData(5, 0, 8)]
    [InlineData(5, 30, 0)]
    [InlineData(-1, 30, 8)]
    public void Non_positive_lifetimes_fail(int accessMinutes, int idleMinutes, int absoluteHours)
    {
        var result = new AuthOptionsValidator().Validate(null, new AuthOptions
        {
            AccessTokenMinutes = accessMinutes,
            IdleTimeoutMinutes = idleMinutes,
            AbsoluteSessionHours = absoluteHours,
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Default_lifetimes_are_valid()
        => Assert.True(new AuthOptionsValidator().Validate(null, new AuthOptions()).Succeeded);

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(JwtOptions options)
        => new JwtOptionsValidator().Validate(null, options);
}
