using SvmApi.Infrastructure.Auth;

namespace SvmApi.Tests.Auth;

/// <summary>RefreshCredential (US-001/003): encode/parse, hash เก็บฝั่ง server, constant-time compare.</summary>
public class RefreshCredentialTests
{
    [Fact]
    public void Encode_and_parse_round_trip()
    {
        var sessionId = Guid.NewGuid();
        var credential = RefreshCredential.Generate(sessionId);

        var ok = RefreshCredential.TryParse(credential.Encode(), out var parsed);

        Assert.True(ok);
        Assert.NotNull(parsed);
        Assert.Equal(sessionId, parsed.SessionId);
        Assert.Equal(credential.Secret, parsed.Secret);
        Assert.Equal(credential.ComputeHash(), parsed.ComputeHash());
    }

    [Fact]
    public void Generate_never_reuses_a_secret_for_the_same_session()
    {
        var sessionId = Guid.NewGuid();

        var secrets = Enumerable.Range(0, 200)
            .Select(_ => RefreshCredential.Generate(sessionId).Secret)
            .ToList();

        Assert.Equal(secrets.Count, secrets.Distinct().Count());
    }

    [Fact]
    public void Generated_token_has_expected_shape()
    {
        var sessionId = Guid.NewGuid();
        var token = RefreshCredential.Generate(sessionId).Encode();

        Assert.StartsWith(sessionId.ToString("N") + ".", token);
        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("no-separator-0000000000000000000000000000000000000000000")]
    [InlineData(".")]
    [InlineData("00000000000000000000000000000000.")]
    [InlineData("00000000000000000000000000000000.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // session id ว่าง
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // session id ไม่ใช่ hex
    public void TryParse_rejects_malformed_tokens(string? token)
    {
        Assert.False(RefreshCredential.TryParse(token, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_rejects_wrong_secret_length_bad_alphabet_extra_parts_and_oversize()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var valid = RefreshCredential.Generate(Guid.Parse(sessionId)).Secret;

        Assert.False(RefreshCredential.TryParse($"{sessionId}.{valid[..^4]}", out _)); // สั้นเกิน
        Assert.False(RefreshCredential.TryParse($"{sessionId}.{valid}AAAA", out _)); // ยาวเกิน
        Assert.False(RefreshCredential.TryParse($"{sessionId}.{valid[..^1]}!", out _)); // อักขระนอก base64url
        Assert.False(RefreshCredential.TryParse($"{sessionId}.{valid}.extra", out _)); // เกินหนึ่งตัวคั่น
        Assert.False(RefreshCredential.TryParse($"{sessionId}.{new string('A', 500)}", out _)); // ยาวเกินกำหนด
    }

    [Fact]
    public void Hash_is_deterministic_lowercase_hex_sha256()
    {
        var first = RefreshCredential.HashSecret("secret-value");
        var second = RefreshCredential.HashSecret("secret-value");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.NotEqual(first, RefreshCredential.HashSecret("secret-value2"));
    }

    [Fact]
    public void Hash_matches_known_sha256_vector()
    {
        // SHA-256("abc")
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            RefreshCredential.HashSecret("abc"));
    }

    [Fact]
    public void Hash_does_not_contain_the_secret()
    {
        var credential = RefreshCredential.Generate(Guid.NewGuid());

        Assert.DoesNotContain(credential.Secret, credential.ComputeHash());
    }

    [Fact]
    public void HashEquals_compares_values_and_rejects_null_or_different_length()
    {
        var hash = RefreshCredential.HashSecret("abc");

        Assert.True(RefreshCredential.HashEquals(hash, hash));
        Assert.True(RefreshCredential.HashEquals(hash, new string(hash.AsSpan())));
        Assert.False(RefreshCredential.HashEquals(hash, RefreshCredential.HashSecret("abd")));
        Assert.False(RefreshCredential.HashEquals(null, hash));
        Assert.False(RefreshCredential.HashEquals(hash, hash[..^1]));
        Assert.False(RefreshCredential.HashEquals(hash, string.Empty));
    }

    [Fact]
    public void ToString_does_not_leak_the_secret()
    {
        var credential = RefreshCredential.Generate(Guid.NewGuid());

        Assert.DoesNotContain(credential.Secret, credential.ToString());
    }
}
