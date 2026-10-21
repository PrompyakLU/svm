using System.Text.Json.Nodes;
using SvmApi.Infrastructure.Audit;

namespace SvmApi.Tests.Audit;

/// <summary>ทดสอบการปิดบังค่าอ่อนไหวก่อนเก็บ Before/After (NFR-20, BR-33).</summary>
public class AuditRedactorTests
{
    private static JsonNode Parse(string? json) => JsonNode.Parse(json!)!;

    [Fact]
    public void Redact_Null_ReturnsNull()
    {
        Assert.Null(AuditRedactor.Redact(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Redact_EmptyOrWhitespace_ReturnedAsIs(string input)
    {
        Assert.Equal(input, AuditRedactor.Redact(input));
    }

    [Theory]
    [InlineData("plain text password=abc")]
    [InlineData("Open")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("\"password\"")]
    [InlineData("{not valid json, password: 1")]
    [InlineData("[1, 2,")]
    public void Redact_NonJsonOrUnparseable_ReturnedUnchanged(string input)
    {
        Assert.Equal(input, AuditRedactor.Redact(input));
    }

    [Fact]
    public void Redact_NoSensitiveKeys_ReturnsOriginalTextExactly()
    {
        const string input = "{ \"status\": \"Open\",   \"count\": 3 }";
        Assert.Same(input, AuditRedactor.Redact(input));
    }

    [Fact]
    public void Redact_MasksTopLevelSensitiveValues()
    {
        var output = AuditRedactor.Redact("{\"username\":\"u1\",\"password\":\"hunter2\"}");
        var node = Parse(output);

        Assert.Equal("u1", (string?)node["username"]);
        Assert.Equal("***", (string?)node["password"]);
        Assert.DoesNotContain("hunter2", output);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("accessToken")]
    [InlineData("refresh_token")]
    [InlineData("clientSecret")]
    [InlineData("Authorization")]
    [InlineData("Set-Cookie")]
    [InlineData("passwordHash")]
    [InlineData("apiKey")]
    [InlineData("api_key")]
    [InlineData("credentials")]
    public void Redact_MasksKnownSensitiveKeyVariants(string key)
    {
        var output = AuditRedactor.Redact($"{{\"{key}\":\"s3cr3t-value\"}}");

        Assert.DoesNotContain("s3cr3t-value", output);
        Assert.Equal("***", (string?)Parse(output)[key]);
    }

    [Fact]
    public void Redact_MasksNestedObjectsAndArrays()
    {
        const string input =
            "{\"user\":{\"name\":\"a\",\"token\":\"t1\"},\"items\":[{\"id\":1,\"secret\":\"s1\"},{\"id\":2}]}";

        var node = Parse(AuditRedactor.Redact(input));

        Assert.Equal("a", (string?)node["user"]!["name"]);
        Assert.Equal("***", (string?)node["user"]!["token"]);
        Assert.Equal(1, (int?)node["items"]![0]!["id"]);
        Assert.Equal("***", (string?)node["items"]![0]!["secret"]);
        Assert.Equal(2, (int?)node["items"]![1]!["id"]);
    }

    [Fact]
    public void Redact_MasksNonStringValuesAndWholeSubtrees()
    {
        var node = Parse(AuditRedactor.Redact("{\"credential\":{\"user\":\"x\",\"pass\":\"y\"},\"pinKey\":12345}"));

        Assert.Equal("***", (string?)node["credential"]);
        Assert.Equal("***", (string?)node["pinKey"]);
    }

    [Fact]
    public void Redact_TopLevelArray()
    {
        var node = Parse(AuditRedactor.Redact("[{\"password\":\"p\"},{\"ok\":true}]"));

        Assert.Equal("***", (string?)node[0]!["password"]);
        Assert.True((bool?)node[1]!["ok"]);
    }

    [Fact]
    public void Redact_NullSensitiveValueStaysNull()
    {
        var output = AuditRedactor.Redact("{\"password\":null,\"token\":\"t\"}");
        var node = Parse(output);

        Assert.Null(node["password"]);
        Assert.Equal("***", (string?)node["token"]);
    }

    [Fact]
    public void Redact_PreservesThaiText()
    {
        var output = AuditRedactor.Redact("{\"note\":\"ทดสอบ\",\"password\":\"x\"}");

        Assert.Contains("ทดสอบ", output);
        Assert.DoesNotContain("\\u0E", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redact_IsIdempotent()
    {
        var once = AuditRedactor.Redact("{\"password\":\"p\",\"a\":1}");
        var twice = AuditRedactor.Redact(once);

        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData("password", true)]
    [InlineData("PASSWORD", true)]
    [InlineData("userName", false)]
    [InlineData("status", false)]
    [InlineData("", false)]
    public void IsSensitiveKey_Matches(string key, bool expected)
    {
        Assert.Equal(expected, AuditRedactor.IsSensitiveKey(key));
    }
}
