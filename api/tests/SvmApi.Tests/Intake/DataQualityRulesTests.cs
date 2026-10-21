using SvmApi.Application.Intake;
using SvmApi.Application.Reference;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Tests.Intake;

/// <summary>Dedup key/matcher (US-012 · BR-12 · EX-45), reference policy (EX-16/47/48/49), idempotency (BR-36 · EX-17).</summary>
public class DataQualityRulesTests
{
    private static readonly Guid Asset = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherAsset = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Cve = "CVE-2021-44228";

    private static DedupKey Key(string? cve = Cve, string? location = "https://app/login", int? port = 443, string? component = "log4j", Guid? asset = null)
        => new(asset ?? Asset, cve, location, port, component);

    private static DedupCandidate Candidate(
        DedupKey key, string title = "Log4Shell", VulnerabilityStatus status = VulnerabilityStatus.New, Guid? id = null)
        => new(id ?? Guid.NewGuid(), key, title, status);

    // ------------------------------------------------------------ dedup key

    [Fact]
    public void DedupKey_IsCaseInsensitive_TrimmedAndNullEqual()
    {
        Assert.Equal(Key(cve: "cve-2021-44228", location: " HTTPS://APP/LOGIN ", component: "LOG4J"), Key());
        Assert.Equal(Key(cve: null, location: null, port: null, component: "  "), Key(cve: null, location: null, port: null, component: null));
        Assert.Equal(Key(component: "LOG4J").GetHashCode(), Key().GetHashCode());
    }

    [Theory]
    [InlineData("CVE-2021-45046", "https://app/login", 443, "log4j")]
    [InlineData(Cve, "https://app/admin", 443, "log4j")]
    [InlineData(Cve, "https://app/login", 8443, "log4j")]
    [InlineData(Cve, "https://app/login", 443, "spring")]
    public void DedupKey_AnyFieldDifferent_IsDifferent(string cve, string location, int port, string component)
        => Assert.NotEqual(Key(), Key(cve, location, port, component));

    [Fact]
    public void DedupKey_DifferentAsset_IsDifferent()
        => Assert.NotEqual(Key(), Key(asset: OtherAsset));

    [Fact]
    public void Fingerprint_IsStable_AndDoesNotExposeLocation()
    {
        var fp = Key().Fingerprint;
        Assert.Equal(16, fp.Length);
        Assert.Equal(fp, Key(location: "HTTPS://APP/LOGIN").Fingerprint);
        Assert.DoesNotContain("app", fp, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(fp, Key(port: 80).Fingerprint);
    }

    // ------------------------------------------------------------ matcher

    [Fact]
    public void Match_NoCandidates_IsNew()
        => Assert.Equal(DedupMatchKind.New, DedupMatcher.Match(Key(), "Log4Shell", []).Kind);

    [Fact]
    public void Match_ExactKey_IsDuplicateOfThatInstance()
    {
        var hit = Candidate(Key(component: "LOG4J"));
        var other = Candidate(Key(location: "https://app/other"));

        var m = DedupMatcher.Match(Key(), "Different scanner title", [other, hit]);

        Assert.Equal(DedupMatchKind.Exact, m.Kind);
        Assert.Equal(hit.InstanceId, m.InstanceId);
        Assert.False(m.IsRecurrence);
    }

    [Fact]
    public void Match_IsIdempotent_ReimportOfSameFindingLinksToSameInstance()
    {
        var existing = Candidate(Key());
        var first = DedupMatcher.Match(Key(), "Log4Shell", [existing]);
        var second = DedupMatcher.Match(Key(), "Log4Shell", [existing]);

        Assert.Equal(DedupMatchKind.Exact, first.Kind);
        Assert.Equal(first.Kind, second.Kind);
        Assert.Equal(existing.InstanceId, first.InstanceId);
        Assert.Equal(existing.InstanceId, second.InstanceId);
    }

    [Theory]
    [InlineData(VulnerabilityStatus.Closed, true)]
    [InlineData(VulnerabilityStatus.Resolved, true)]
    [InlineData(VulnerabilityStatus.InProgress, false)]
    public void Match_ExactOnClosedInstance_FlagsRecurrence(VulnerabilityStatus status, bool recurrence)
    {
        var m = DedupMatcher.Match(Key(), "Log4Shell", [Candidate(Key(), status: status)]);

        Assert.Equal(DedupMatchKind.Exact, m.Kind);
        Assert.Equal(recurrence, m.IsRecurrence);
    }

    [Fact]
    public void Match_ArchivedOrOtherAssetOrOtherCve_AreIgnored()
    {
        var m = DedupMatcher.Match(Key(), "Log4Shell",
        [
            Candidate(Key(), status: VulnerabilityStatus.Archived),
            Candidate(Key(asset: OtherAsset)),
            Candidate(Key(cve: "CVE-2021-45046")),
        ]);

        Assert.Equal(DedupMatchKind.New, m.Kind);
    }

    [Theory]
    [InlineData(null, 443, "log4j")] // finding ไม่ระบุ location
    [InlineData("https://app/login", null, "log4j")] // ไม่ระบุ port
    [InlineData("https://app/login", 443, null)] // ไม่ระบุ component
    public void Match_PartialMatch_IsAmbiguous_EX45(string? location, int? port, string? component)
    {
        var existing = Candidate(Key());
        var m = DedupMatcher.Match(Key(location: location, port: port, component: component), "Log4Shell", [existing]);

        Assert.Equal(DedupMatchKind.Ambiguous, m.Kind);
        Assert.Null(m.InstanceId);
        Assert.Equal(existing.InstanceId, Assert.Single(m.CandidateIds));
    }

    [Fact]
    public void Match_ExistingHasMissingField_IsAlsoAmbiguous()
    {
        var existing = Candidate(Key(port: null));
        Assert.Equal(DedupMatchKind.Ambiguous, DedupMatcher.Match(Key(), "x", [existing]).Kind);
    }

    [Fact]
    public void Match_SeveralPartialCandidates_AreAllQueued()
    {
        var a = Candidate(Key(port: 443, component: "log4j"));
        var b = Candidate(Key(port: 8443, component: "log4j"));

        var m = DedupMatcher.Match(Key(port: null), "Log4Shell", [a, b]);

        Assert.Equal(DedupMatchKind.Ambiguous, m.Kind);
        Assert.Equal(2, m.CandidateIds.Count);
    }

    [Fact]
    public void Match_ConflictingNonNullField_IsNew()
    {
        var existing = Candidate(Key(port: 8443));
        Assert.Equal(DedupMatchKind.New, DedupMatcher.Match(Key(port: 443), "Log4Shell", [existing]).Kind);
    }

    [Fact]
    public void Match_WithoutCve_SameKeySameTitle_IsExact()
    {
        var existing = Candidate(Key(cve: null), title: "Reflected  XSS");
        var m = DedupMatcher.Match(Key(cve: null), "reflected xss", [existing]);

        Assert.Equal(DedupMatchKind.Exact, m.Kind);
        Assert.Equal(existing.InstanceId, m.InstanceId);
    }

    [Fact]
    public void Match_WithoutCve_SameKeyDifferentTitle_IsAmbiguous()
    {
        var existing = Candidate(Key(cve: null), title: "Reflected XSS");
        var m = DedupMatcher.Match(Key(cve: null), "SQL Injection", [existing]);

        Assert.Equal(DedupMatchKind.Ambiguous, m.Kind);
    }

    [Fact]
    public void Match_WithoutCve_PartialDifferentTitle_IsNew()
    {
        var existing = Candidate(Key(cve: null, port: 443), title: "Reflected XSS");
        Assert.Equal(DedupMatchKind.New, DedupMatcher.Match(Key(cve: null, port: null), "SQL Injection", [existing]).Kind);
    }

    // ------------------------------------------------------------ reference policy

    private static ReferenceEnrichment Enrichment(params string[] issues)
        => new(Cve, null, null, null, "3.1", null, true, true, false, issues);

    [Theory]
    [InlineData(ReferenceIssueCodes.CveIdInvalid, "cveId")]
    [InlineData(ReferenceIssueCodes.CweIdInvalid, "cweId")]
    [InlineData(ReferenceIssueCodes.CvssScoreInvalid, "cvssBaseScore")]
    public void ReferencePolicy_TypeErrors_AreRejected_EX16(string issue, string field)
    {
        var d = ReferencePolicy.Evaluate(Enrichment(ReferenceIssueCodes.CveNotFound, issue));

        Assert.True(d.Rejected);
        Assert.Contains(d.Rejections, e => e.Field == field);
        Assert.False(d.NeedsReview);
    }

    [Theory]
    [InlineData(ReferenceIssueCodes.CveNotFound)]
    [InlineData(ReferenceIssueCodes.CweNotFound)]
    [InlineData(ReferenceIssueCodes.CvssVersionUnsupported)]
    [InlineData(ReferenceIssueCodes.CvssVersionMissing)]
    [InlineData(ReferenceIssueCodes.CvssVectorInvalid)]
    public void ReferencePolicy_LookupIssues_AreStoredAsNeedsReview(string issue)
    {
        var d = ReferencePolicy.Evaluate(Enrichment(issue));

        Assert.False(d.Rejected);
        Assert.True(d.NeedsReview);
        Assert.Equal(issue, Assert.Single(d.ReviewIssues));
    }

    [Fact]
    public void ReferencePolicy_NoIssues_IsClean()
    {
        var d = ReferencePolicy.Evaluate(Enrichment());
        Assert.False(d.Rejected);
        Assert.False(d.NeedsReview);
    }

    [Theory]
    [InlineData(false, false, ValidationStatus.Valid)]
    [InlineData(true, false, ValidationStatus.NeedsReview)]
    [InlineData(false, true, ValidationStatus.NeedsReview)]
    [InlineData(true, true, ValidationStatus.NeedsReview)]
    public void NeedsReviewRule(bool ambiguous, bool referenceIssue, ValidationStatus expected)
        => Assert.Equal(expected, ReferencePolicy.StatusFor(ambiguous, referenceIssue));

    // ------------------------------------------------------------ idempotency

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("   ", true, null)]
    [InlineData(" tx-2026.10:01_A ", true, "tx-2026.10:01_A")]
    [InlineData("has space", false, null)]
    [InlineData("<script>", false, null)]
    public void TransactionId_IsNormalizedAndValidated(string? raw, bool valid, string? expected)
    {
        var (isValid, value) = ImportIdempotency.NormalizeTransactionId(raw);
        Assert.Equal(valid, isValid);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TransactionId_TooLong_IsInvalid()
        => Assert.False(ImportIdempotency.NormalizeTransactionId(new string('a', FindingLimits.TransactionId + 1)).IsValid);

    [Fact]
    public void Idempotency_Decisions()
    {
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var batch = Guid.NewGuid();

        Assert.Equal(IdempotencyDecision.Proceed, ImportIdempotency.Decide(null, me));
        Assert.Equal(IdempotencyDecision.Replay, ImportIdempotency.Decide(new ExistingBatch(batch, me, ImportBatchStatus.Partial), me));
        Assert.Equal(IdempotencyDecision.Replay, ImportIdempotency.Decide(new ExistingBatch(batch, me, ImportBatchStatus.Failed), me));
        Assert.Equal(IdempotencyDecision.InProgress, ImportIdempotency.Decide(new ExistingBatch(batch, me, ImportBatchStatus.Processing), me));
        Assert.Equal(IdempotencyDecision.Conflict, ImportIdempotency.Decide(new ExistingBatch(batch, other, ImportBatchStatus.Success), me));
    }

    [Theory]
    [InlineData(10, 10, ImportBatchStatus.Success)]
    [InlineData(10, 7, ImportBatchStatus.Partial)]
    [InlineData(10, 0, ImportBatchStatus.Failed)]
    [InlineData(0, 0, ImportBatchStatus.Failed)]
    public void BatchStatus_FromCounts_EX18(int total, int success, ImportBatchStatus expected)
        => Assert.Equal(expected, ImportIdempotency.StatusFor(total, success));
}
