using SvmApi.Application.Reference;

namespace SvmApi.Tests.Reference;

/// <summary>EX-47 (ละเอียดขึ้น): id ผิดรูปแบบได้ทั้ง *_NOT_FOUND และ *_ID_INVALID; id ถูกรูปแบบแต่ไม่มีใน seed ได้แค่ *_NOT_FOUND.</summary>
public class ReferenceIdValidationTests
{
    private static readonly ReferenceSnapshot Snapshot = new(
        [new CveDto("CVE-2021-44228", "Log4Shell", null, true, null)],
        [new CweDto("CWE-79", "Cross-site Scripting", null)],
        new Dictionary<string, bool> { ["3.1"] = true, ["4.0"] = true, ["2.0"] = false });

    private static ReferenceEnrichment Run(string? cve = null, string? cwe = null)
        => ReferenceEnricher.Enrich(Snapshot, new ReferenceEnrichmentRequest(cve, cwe, null, null));

    [Theory]
    [InlineData("not-a-cve")]
    [InlineData("CVE-21-44228")]
    [InlineData("CVE-2021-44228; DROP TABLE x")]
    [InlineData("CVE-2021-1234567890123456789012345")] // ยาวเกิน 30
    public void Enrich_MalformedCve_MarksNotFoundAndInvalid_KeepsValue(string cve)
    {
        var r = Run(cve);

        Assert.Equal(cve, r.CveId);
        Assert.Null(r.Cve);
        Assert.False(r.IsKev);
        Assert.Equal([ReferenceIssueCodes.CveNotFound, ReferenceIssueCodes.CveIdInvalid], r.Issues);
    }

    [Fact]
    public void Enrich_WellFormedUnknownCve_OnlyNotFound()
    {
        var r = Run("CVE-1999-0001");

        Assert.Equal([ReferenceIssueCodes.CveNotFound], r.Issues);
        Assert.False(r.HasIssue(ReferenceIssueCodes.CveIdInvalid));
    }

    [Theory]
    [InlineData("CWE-abc")]
    [InlineData("XSS")]
    [InlineData("12345678")] // ตัวเลขยาวเกิน -> ไม่เติม prefix -> ผิดรูปแบบ
    public void Enrich_MalformedCwe_MarksNotFoundAndInvalid(string cwe)
    {
        var r = Run(cwe: cwe);

        Assert.Null(r.Cwe);
        Assert.Equal([ReferenceIssueCodes.CweNotFound, ReferenceIssueCodes.CweIdInvalid], r.Issues);
    }

    [Theory]
    [InlineData("CWE-9999")]
    [InlineData("9999")] // เติม prefix -> ถูกรูปแบบ
    public void Enrich_WellFormedUnknownCwe_OnlyNotFound(string cwe)
    {
        var r = Run(cwe: cwe);

        Assert.Equal([ReferenceIssueCodes.CweNotFound], r.Issues);
    }

    [Fact]
    public void Enrich_KnownIds_NoIdIssues()
    {
        var r = Run("cve-2021-44228", "79");

        Assert.Empty(r.Issues);
        Assert.True(r.IsKev);
    }
}
