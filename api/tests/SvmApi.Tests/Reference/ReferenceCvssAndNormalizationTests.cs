using SvmApi.Application.Reference;

namespace SvmApi.Tests.Reference;

/// <summary>FR-14 (score/severity), id/version normalization และ contract ของ Enrich ที่ Intake/RiskScoring ใช้.</summary>
public class ReferenceCvssAndNormalizationTests
{
    private const string ValidV31 = "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H";

    private static readonly ReferenceSnapshot Snapshot = new(
        [new CveDto("CVE-2021-44228", "Log4Shell", null, true, null)],
        [new CweDto("CWE-79", "Cross-site Scripting", null)],
        new Dictionary<string, bool> { ["3.1"] = true, ["4.0"] = true, ["2.0"] = false });

    private static ReferenceEnrichment Run(
        string? cve = null, string? cwe = null, string? version = null, string? vector = null, decimal? score = null)
        => ReferenceEnricher.Enrich(Snapshot, new ReferenceEnrichmentRequest(cve, cwe, version, vector, score));

    // --- id normalization ---

    [Fact]
    public void Enrich_KnownIds_ReturnCanonicalCaseFromMaster()
    {
        var r = Run(" cve-2021-44228 ", "cwe-79");

        Assert.Equal("CVE-2021-44228", r.CveId);
        Assert.Equal("CWE-79", r.CweId);
    }

    [Fact]
    public void Enrich_NumericCwe_PrefixedAndLinked()
    {
        var r = Run(cwe: "79");

        Assert.Equal("CWE-79", r.CweId);
        Assert.NotNull(r.Cwe);
        Assert.Empty(r.Issues);
    }

    [Theory]
    [InlineData("79", "CWE-79")]
    [InlineData(" 1021 ", "CWE-1021")]
    [InlineData("CWE-79", "CWE-79")]
    [InlineData("cwe-79", "cwe-79")]
    [InlineData("12345678", "12345678")] // ยาวเกินรูปแบบ CWE -> คงค่าเดิม
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeCweId(string? input, string? expected)
        => Assert.Equal(expected, ReferenceIds.NormalizeCweId(input));

    [Theory]
    [InlineData(" CVE-2021-44228 ", "CVE-2021-44228")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeCveId(string? input, string? expected)
        => Assert.Equal(expected, ReferenceIds.NormalizeCveId(input));

    [Theory]
    [InlineData("3.1", "3.1")]
    [InlineData(" v3.1 ", "3.1")]
    [InlineData("V4.0", "4.0")]
    [InlineData("CVSS:3.1", "3.1")]
    [InlineData("CVSSv3.1", "3.1")]
    [InlineData("cvss 4.0", "4.0")]
    [InlineData("CVSS", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeCvssVersion(string? input, string? expected)
        => Assert.Equal(expected, ReferenceIds.NormalizeCvssVersion(input));

    [Fact]
    public void Enrich_PrefixedVersion_TreatedAsSupported()
    {
        var r = Run(version: "CVSSv3.1", vector: ValidV31, score: 9.8m);

        Assert.Equal("3.1", r.CvssVersion);
        Assert.True(r.CvssVersionSupported);
        Assert.True(r.CvssVectorValid);
        Assert.Empty(r.Issues);
    }

    // --- FR-14: score + severity ---

    [Theory]
    [InlineData("0.0", CvssSeverities.None)]
    [InlineData("0.1", CvssSeverities.Low)]
    [InlineData("3.9", CvssSeverities.Low)]
    [InlineData("4.0", CvssSeverities.Medium)]
    [InlineData("6.9", CvssSeverities.Medium)]
    [InlineData("7.0", CvssSeverities.High)]
    [InlineData("8.9", CvssSeverities.High)]
    [InlineData("9.0", CvssSeverities.Critical)]
    [InlineData("10.0", CvssSeverities.Critical)]
    public void CvssScore_Severity_Boundaries(string score, string expected)
        => Assert.Equal(expected, CvssScore.Severity(decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("-0.1")]
    [InlineData("10.1")]
    [InlineData("100")]
    [InlineData("7.55")]
    public void CvssScore_Invalid(string score)
    {
        var s = decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(CvssScore.IsValid(s));
        Assert.Null(CvssScore.Severity(s));
    }

    [Fact]
    public void Enrich_ValidScore_SetsScoreAndSeverity()
    {
        var r = Run(version: "3.1", vector: ValidV31, score: 9.8m);

        Assert.Equal(9.8m, r.CvssBaseScore);
        Assert.Equal(CvssSeverities.Critical, r.CvssSeverity);
        Assert.False(r.NeedsReview);
    }

    [Fact]
    public void Enrich_ScoreWithVersionNoVector_SetsSeverity()
    {
        var r = Run(version: "4.0", score: 5.0m);

        Assert.Equal(CvssSeverities.Medium, r.CvssSeverity);
        Assert.Empty(r.Issues);
    }

    [Theory]
    [InlineData("10.5")]
    [InlineData("-1")]
    [InlineData("7.25")]
    public void Enrich_InvalidScore_MarkedAndDropped(string score)
    {
        var r = Run(version: "3.1", vector: ValidV31,
            score: decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Null(r.CvssBaseScore);
        Assert.Null(r.CvssSeverity);
        Assert.Contains(ReferenceIssueCodes.CvssScoreInvalid, r.Issues);
        Assert.True(r.NeedsReview);
    }

    [Fact]
    public void Enrich_UnsupportedVersion_KeepsScoreButNoSeverity()
    {
        var r = Run(version: "2.0", score: 7.5m);

        Assert.Equal(7.5m, r.CvssBaseScore);
        Assert.Null(r.CvssSeverity);
        Assert.Equal([ReferenceIssueCodes.CvssVersionUnsupported], r.Issues);
    }

    [Fact]
    public void Enrich_ScoreWithoutVersionOrVector_MarksVersionMissing()
    {
        var r = Run(score: 7.5m);

        Assert.Null(r.CvssVersion);
        Assert.Null(r.CvssSeverity);
        Assert.Contains(ReferenceIssueCodes.CvssVersionMissing, r.Issues);
    }

    [Fact]
    public void Enrich_VectorOverMaxLength_Invalid()
    {
        var vector = ValidV31 + string.Concat(Enumerable.Repeat("/E:P", 50));
        Assert.True(vector.Length > CvssVectorValidator.MaxVectorLength);

        var r = Run(version: "3.1", vector: vector);

        Assert.Contains(ReferenceIssueCodes.CvssVectorInvalid, r.Issues);
    }

    // --- US-014: KEV via enrich and IsKev agree ---

    [Theory]
    [InlineData("CVE-2021-44228")]
    [InlineData("cve-2021-44228")]
    [InlineData("CVE-1999-0001")]
    [InlineData(null)]
    public void Enrich_IsKev_ConsistentWith_IsKevLookup(string? cve)
        => Assert.Equal(ReferenceEnricher.IsKev(Snapshot, cve), Run(cve).IsKev);
}
