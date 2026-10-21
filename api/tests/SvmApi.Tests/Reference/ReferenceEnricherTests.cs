using SvmApi.Application.Reference;

namespace SvmApi.Tests.Reference;

/// <summary>ทดสอบตรรกะ enrich/KEV แบบ pure (US-013/014 · EX-47/48/49) — ไม่ใช้ DB.</summary>
public class ReferenceEnricherTests
{
    private const string ValidV31 = "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H";
    private const string ValidV40 = "CVSS:4.0/AV:N/AC:L/AT:N/PR:N/UI:N/VC:H/VI:H/VA:H/SC:N/SI:N/SA:N";

    private static readonly ReferenceSnapshot Snapshot = new(
        [
            new CveDto("CVE-2021-44228", "Log4Shell", new DateTime(2021, 12, 10, 0, 0, 0, DateTimeKind.Utc), true,
                new DateTime(2021, 12, 10, 0, 0, 0, DateTimeKind.Utc)),
            new CveDto("CVE-2016-2183", "SWEET32", new DateTime(2016, 9, 1, 0, 0, 0, DateTimeKind.Utc), false, null),
        ],
        [new CweDto("CWE-79", "Cross-site Scripting", null)],
        new Dictionary<string, bool> { ["3.1"] = true, ["4.0"] = true, ["2.0"] = false });

    private static ReferenceEnrichment Run(
        string? cve = null, string? cwe = null, string? version = null, string? vector = null)
        => ReferenceEnricher.Enrich(Snapshot, new ReferenceEnrichmentRequest(cve, cwe, version, vector));

    // --- US-013: linking ---

    [Fact]
    public void Enrich_KnownCveAndCwe_LinkedWithoutIssues()
    {
        var r = Run("CVE-2016-2183", "CWE-79", "3.1", ValidV31);

        Assert.Equal("SWEET32", r.Cve!.Description);
        Assert.Equal("Cross-site Scripting", r.Cwe!.Name);
        Assert.True(r.CvssVersionSupported);
        Assert.True(r.CvssVectorValid);
        Assert.False(r.NeedsReview);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Enrich_NothingProvided_NoIssues()
    {
        var r = Run();

        Assert.Null(r.Cve);
        Assert.Null(r.Cwe);
        Assert.False(r.IsKev);
        Assert.False(r.NeedsReview);
    }

    [Fact]
    public void Enrich_IsCaseAndWhitespaceInsensitive()
    {
        var r = Run("  cve-2021-44228 ", " cwe-79");

        Assert.NotNull(r.Cve);
        Assert.NotNull(r.Cwe);
        Assert.False(r.NeedsReview);
    }

    // --- EX-47: CVE not found -> keep value + mark ---

    [Theory]
    [InlineData("CVE-1999-0001")]
    [InlineData("not-a-cve")]
    public void Enrich_UnknownCve_KeepsValueAndMarks(string cve)
    {
        var r = Run(cve);

        Assert.Equal(cve, r.CveId);
        Assert.Null(r.Cve);
        Assert.False(r.IsKev);
        Assert.Contains(ReferenceIssueCodes.CveNotFound, r.Issues);
        Assert.True(r.NeedsReview);
    }

    [Fact]
    public void Enrich_UnknownCwe_KeepsValueAndMarks()
    {
        var r = Run(cwe: "CWE-9999");

        Assert.Equal("CWE-9999", r.CweId);
        Assert.Null(r.Cwe);
        Assert.Contains(ReferenceIssueCodes.CweNotFound, r.Issues);
    }

    // --- EX-48: unsupported CVSS version ---

    [Theory]
    [InlineData("2.0")]   // อยู่ใน config แต่ isSupported = false
    [InlineData("9.9")]   // ไม่อยู่ใน config
    public void Enrich_UnsupportedCvssVersion_Marks(string version)
    {
        var r = Run(version: version, vector: "AV:N/AC:L/Au:N/C:P/I:P/A:P");

        Assert.False(r.CvssVersionSupported);
        Assert.Contains(ReferenceIssueCodes.CvssVersionUnsupported, r.Issues);
        Assert.DoesNotContain(ReferenceIssueCodes.CvssVectorInvalid, r.Issues);
    }

    // --- EX-49: malformed vector ---

    [Theory]
    [InlineData("3.1", "garbage")]
    [InlineData("3.1", "CVSS:3.1/AV:N/AC:L")]                                   // metric ไม่ครบ
    [InlineData("3.1", "CVSS:3.1/AV:X/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H")]          // ค่า metric ผิด
    [InlineData("3.1", "AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H")]                   // ไม่มี prefix
    [InlineData("3.1", ValidV40)]                                                // prefix ไม่ตรง version
    [InlineData("4.0", ValidV31)]
    [InlineData("3.1", ValidV31 + "/<script>")]                                  // ท้ายเป็นอักขระต้องห้าม
    public void Enrich_MalformedVector_Marks(string version, string vector)
    {
        var r = Run(version: version, vector: vector);

        Assert.True(r.CvssVersionSupported);
        Assert.False(r.CvssVectorValid);
        Assert.Contains(ReferenceIssueCodes.CvssVectorInvalid, r.Issues);
        Assert.Equal(vector, r.CvssVector);
    }

    [Theory]
    [InlineData("3.1", ValidV31)]
    [InlineData("3.1", ValidV31 + "/E:P/RL:O/RC:C")]
    [InlineData("4.0", ValidV40)]
    public void Enrich_ValidVector_Accepted(string version, string vector)
    {
        var r = Run(version: version, vector: vector);

        Assert.True(r.CvssVectorValid);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Enrich_VersionInferredFromVectorPrefix()
    {
        var r = Run(vector: ValidV40);

        Assert.Equal("4.0", r.CvssVersion);
        Assert.True(r.CvssVersionSupported);
        Assert.True(r.CvssVectorValid);
    }

    [Fact]
    public void Enrich_VectorWithoutVersionOrPrefix_Marks()
    {
        var r = Run(vector: "AV:N/AC:L/PR:N");

        Assert.Contains(ReferenceIssueCodes.CvssVersionMissing, r.Issues);
    }

    [Fact]
    public void Enrich_MultipleProblems_AllReported_NoThrow()
    {
        var r = Run("CVE-1999-0001", "CWE-9999", "9.9", "x");

        Assert.Equal(
            [ReferenceIssueCodes.CveNotFound, ReferenceIssueCodes.CweNotFound, ReferenceIssueCodes.CvssVersionUnsupported],
            r.Issues);
    }

    [Fact]
    public void Enrich_NullSnapshotOrRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ReferenceEnricher.Enrich(null!, new ReferenceEnrichmentRequest(null, null, null, null)));
        Assert.Throws<ArgumentNullException>(() => ReferenceEnricher.Enrich(Snapshot, null!));
    }

    [Fact]
    public void Enrich_EmptySnapshot_MarksEverythingButDoesNotThrow()
    {
        var r = ReferenceEnricher.Enrich(
            ReferenceSnapshot.Empty,
            new ReferenceEnrichmentRequest("CVE-2021-44228", "CWE-79", "3.1", ValidV31));

        Assert.Equal(
            [ReferenceIssueCodes.CveNotFound, ReferenceIssueCodes.CweNotFound, ReferenceIssueCodes.CvssVersionUnsupported],
            r.Issues);
    }

    // --- US-014: KEV ---

    [Fact]
    public void Enrich_KevCve_SetsIsKev()
    {
        Assert.True(Run("CVE-2021-44228").IsKev);
        Assert.False(Run("CVE-2016-2183").IsKev);
    }

    [Theory]
    [InlineData("CVE-2021-44228", true)]   // KEV -> วันที่จาก master
    [InlineData("CVE-2016-2183", false)]   // ไม่ใช่ KEV
    [InlineData("CVE-1999-0001", false)]   // ไม่รู้จัก (EX-47)
    [InlineData(null, false)]
    public void Enrich_KevDateAdded_OnlyForKev(string? cve, bool hasDate)
    {
        var r = Run(cve);

        if (hasDate)
        {
            Assert.Equal(new DateTime(2021, 12, 10, 0, 0, 0, DateTimeKind.Utc), r.KevDateAdded);
        }
        else
        {
            Assert.Null(r.KevDateAdded);
        }
    }

    [Fact]
    public void Enrich_HasIssue_MatchesIssueCodes()
    {
        var r = Run("CVE-1999-0001", version: "2.0");

        Assert.True(r.HasIssue(ReferenceIssueCodes.CveNotFound));
        Assert.True(r.HasIssue(ReferenceIssueCodes.CvssVersionUnsupported));
        Assert.False(r.HasIssue(ReferenceIssueCodes.CvssVectorInvalid));
        Assert.False(r.HasIssue("cve_not_found")); // ordinal
    }

    [Theory]
    [InlineData("CVE-2021-44228", true)]
    [InlineData("cve-2021-44228", true)]
    [InlineData(" CVE-2021-44228 ", true)]
    [InlineData("CVE-2016-2183", false)]   // มีใน master แต่ไม่ใช่ KEV
    [InlineData("CVE-1999-0001", false)]   // ไม่รู้จัก
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsKev_Lookup(string? cveId, bool expected)
    {
        Assert.Equal(expected, ReferenceEnricher.IsKev(Snapshot, cveId));
    }

    // --- Helpers ---

    [Theory]
    [InlineData("CVE-2021-44228", true)]
    [InlineData("cve-2024-3094", true)]
    [InlineData("CVE-2021-1", false)]
    [InlineData("CVE-21-44228", false)]
    [InlineData("CVE-2021-44228; DROP TABLE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReferenceIds_IsValidCveId(string? id, bool expected)
        => Assert.Equal(expected, ReferenceIds.IsValidCveId(id));

    [Theory]
    [InlineData("CWE-79", true)]
    [InlineData("cwe-1021", true)]
    [InlineData("CWE-", false)]
    [InlineData("CWE-abc", false)]
    [InlineData("79", false)]
    [InlineData(null, false)]
    public void ReferenceIds_IsValidCweId(string? id, bool expected)
        => Assert.Equal(expected, ReferenceIds.IsValidCweId(id));

    [Theory]
    [InlineData("CVSS:3.1/AV:N", "3.1")]
    [InlineData("CVSS:4.0/AV:N", "4.0")]
    [InlineData("AV:N/AC:L", null)]
    [InlineData("CVSS:3.1", null)]
    [InlineData(null, null)]
    public void CvssVectorValidator_InferVersion(string? vector, string? expected)
        => Assert.Equal(expected, CvssVectorValidator.InferVersion(vector));
}
