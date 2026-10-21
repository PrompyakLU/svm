using SvmApi.Application.Intake;

namespace SvmApi.Tests.Intake;

/// <summary>Validation (US-010 · EX-15/16) + normalization (US-011) แบบ pure — ไม่ใช้ DB.</summary>
public class FindingValidatorTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    private static RawFindingRow Row(
        string? assetCode = "SVM-APP-001",
        string? sourceSystem = "Pentest",
        string? findingName = "SQL Injection",
        string? severity = "High",
        string? cveId = null,
        string? cweId = null,
        string? score = null,
        string? location = null,
        string? port = null,
        string? component = null,
        string? detectedAt = null,
        string? cvssVersion = null,
        string? cvssVector = null)
        => new(assetCode, sourceSystem, "SRC-1", findingName, severity, cveId, cweId, cvssVersion, score, cvssVector,
            location, port, component, detectedAt);

    [Fact]
    public void Valid_Row_IsNormalized()
    {
        var r = FindingValidator.Validate(
            Row(assetCode: "  SVM-APP-001 ", findingName: "  SQL   Injection \t in login ", severity: "crit",
                cveId: " cve-2021-44228 ", cweId: "89", score: "9.8", location: "HTTPS://App.Example/Login/#frag",
                port: "443", component: " log4j ", detectedAt: "2026-10-01T08:00:00+07:00"),
            Now);

        Assert.True(r.IsValid);
        var f = r.Value!;
        Assert.Equal("SVM-APP-001", f.AssetCode);
        Assert.Equal("SQL Injection in login", f.FindingName);
        Assert.Equal(SeverityNormalizer.Critical, f.Severity);
        Assert.Equal("CVE-2021-44228", f.CveId);
        Assert.Equal("CWE-89", f.CweId);
        Assert.Equal(9.8m, f.CvssBaseScore);
        Assert.Equal("https://app.example/Login", f.Location);
        Assert.Equal(443, f.Port);
        Assert.Equal("log4j", f.Component);
        Assert.Equal(new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc), f.DetectedAt);
        Assert.Equal(DateTimeKind.Utc, f.DetectedAt.Kind);
    }

    [Fact]
    public void MissingDetectedAt_DefaultsToNow()
    {
        var r = FindingValidator.Validate(Row(), Now);
        Assert.True(r.IsValid);
        Assert.Equal(Now, r.Value!.DetectedAt);
    }

    [Theory]
    [InlineData("assetCode")]
    [InlineData("sourceSystem")]
    [InlineData("findingName")]
    [InlineData("severity")]
    public void MissingMandatory_IsRejectedWithFieldName(string field)
    {
        var row = field switch
        {
            "assetCode" => Row(assetCode: "  "),
            "sourceSystem" => Row(sourceSystem: null),
            "findingName" => Row(findingName: ""),
            _ => Row(severity: null),
        };

        var r = FindingValidator.Validate(row, Now);

        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Field == field && e.Code == IntakeErrorCodes.MissingMandatory);
        Assert.Contains(r.Errors, e => e.Reason == $"MISSING_MANDATORY: {field}");
    }

    [Fact]
    public void NullRow_ReportsAllMandatoryFields()
    {
        var r = FindingValidator.Validate(null, Now);
        Assert.False(r.IsValid);
        Assert.Equal(IntakeTemplate.RequiredNames.Count, r.Errors.Count);
    }

    [Theory]
    [InlineData("port", "abc", IntakeErrorCodes.InvalidType)]
    [InlineData("port", "70000", IntakeErrorCodes.InvalidPort)]
    [InlineData("port", "-1", IntakeErrorCodes.InvalidType)]
    [InlineData("cvssBaseScore", "nine", IntakeErrorCodes.InvalidType)]
    [InlineData("cvssBaseScore", "10.5", IntakeErrorCodes.InvalidScore)]
    [InlineData("cvssBaseScore", "9.85", IntakeErrorCodes.InvalidScore)]
    [InlineData("detectedAt", "yesterday", IntakeErrorCodes.InvalidType)]
    [InlineData("detectedAt", "2030-01-01T00:00:00Z", IntakeErrorCodes.InvalidDate)]
    [InlineData("cveId", "CVE-21-1", IntakeErrorCodes.InvalidIdentifier)]
    [InlineData("cweId", "XSS", IntakeErrorCodes.InvalidIdentifier)]
    [InlineData("severity", "spicy", IntakeErrorCodes.InvalidSeverity)]
    public void WrongType_IsRejected_EX16(string field, string value, string code)
    {
        var row = field switch
        {
            "port" => Row(port: value),
            "cvssBaseScore" => Row(score: value),
            "detectedAt" => Row(detectedAt: value),
            "cveId" => Row(cveId: value),
            "cweId" => Row(cweId: value),
            _ => Row(severity: value),
        };

        var r = FindingValidator.Validate(row, Now);

        Assert.False(r.IsValid);
        var error = Assert.Single(r.Errors);
        Assert.Equal(field, error.Field);
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain(value, error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCharacters_AreRejected()
    {
        var r = FindingValidator.Validate(Row(findingName: "bad\u0007name"), Now);
        Assert.Contains(r.Errors, e => e.Field == IntakeTemplate.FindingName && e.Code == IntakeErrorCodes.InvalidFormat);
    }

    [Fact]
    public void ColumnLimits_AreEnforced()
    {
        var r = FindingValidator.Validate(
            Row(findingName: new string('a', FindingLimits.FindingName + 1),
                cvssVersion: new string('3', FindingLimits.CvssVersion + 1),
                cvssVector: "CVSS:3.1/" + new string('A', FindingLimits.CvssVector)),
            Now);

        Assert.Contains(r.Errors, e => e.Field == IntakeTemplate.FindingName && e.Code == IntakeErrorCodes.TooLong);
        Assert.Contains(r.Errors, e => e.Field == IntakeTemplate.CvssVersion && e.Code == IntakeErrorCodes.TooLong);
        Assert.Contains(r.Errors, e => e.Field == IntakeTemplate.CvssVector && e.Code == IntakeErrorCodes.TooLong);
    }

    [Theory]
    [InlineData("Critical", "Critical")]
    [InlineData("VERY-HIGH", "Critical")]
    [InlineData("5", "Critical")]
    [InlineData("important", "High")]
    [InlineData("Moderate", "Medium")]
    [InlineData("minor", "Low")]
    [InlineData("info", "Informational")]
    [InlineData("0", "Informational")]
    public void Severity_SynonymsMapToCanonical(string raw, string expected)
        => Assert.Equal(expected, SeverityNormalizer.Normalize(raw));

    [Theory]
    [InlineData("https://Host.Example:8443/a/b/?x=1#f", "https://host.example:8443/a/b?x=1")]
    [InlineData("http://host.example/", "http://host.example/")]
    [InlineData("  /var/www/app ", "/var/www/app")]
    [InlineData("10.0.0.5", "10.0.0.5")]
    public void Location_IsNormalized(string raw, string expected)
        => Assert.Equal(expected, LocationNormalizer.Normalize(raw));
}
