using SvmApi.Application.Intake;
using SvmApi.Application.Verification;
using SvmApi.Domain.Verification;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Tests.Verification;

/// <summary>
/// VerificationRules (pure · C10 · US-027/028/029 · BR-13/16 · EX-76..80 · task 5.4):
/// independent verifier, ผล → สถานะ, closure gate ของ Critical, reopen เมื่อ rescan/import พบซ้ำ.
/// </summary>
public class VerificationRulesTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Analyst = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Submitter = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ------------------------------------------------------------ independent verifier (BR-16)

    [Fact]
    public void Independent_when_verifier_is_not_any_responsible_user()
        => Assert.True(VerificationRules.IsIndependent(Analyst, [Owner, Submitter, null]));

    [Fact]
    public void Not_independent_when_verifier_is_the_assignee()
        => Assert.False(VerificationRules.IsIndependent(Owner, [Owner, Submitter]));

    [Fact]
    public void Not_independent_when_verifier_submitted_the_fix()
        => Assert.False(VerificationRules.IsIndependent(Submitter, [Owner, null, Submitter]));

    [Fact]
    public void Independent_when_nobody_is_responsible_yet()
        => Assert.True(VerificationRules.IsIndependent(Analyst, []));

    [Fact]
    public void Empty_verifier_id_is_never_independent()
        => Assert.False(VerificationRules.IsIndependent(Guid.Empty, [Owner]));

    [Theory]
    [InlineData(RiskLevel.Critical, true)]
    [InlineData(RiskLevel.High, false)]
    [InlineData(RiskLevel.Medium, false)]
    [InlineData(RiskLevel.Low, false)]
    [InlineData(RiskLevel.Informational, false)]
    [InlineData(null, false)]
    public void Only_critical_requires_independent_verifier(RiskLevel? level, bool expected)
        => Assert.Equal(expected, VerificationRules.RequiresIndependentVerifier(level));

    // ------------------------------------------------------------ result -> status

    [Theory]
    [InlineData(VerificationResult.Verified, VulnerabilityStatus.Resolved)]
    [InlineData(VerificationResult.NotResolved, VulnerabilityStatus.Reopened)]
    [InlineData(VerificationResult.PartiallyResolved, VulnerabilityStatus.Reopened)]
    public void Result_maps_to_next_status(VerificationResult result, VulnerabilityStatus expected)
        => Assert.Equal(expected, VerificationRules.NextStatus(result));

    [Fact]
    public void Unknown_result_never_closes()
        => Assert.Equal(VulnerabilityStatus.Reopened, VerificationRules.NextStatus((VerificationResult)999));

    // ------------------------------------------------------------ Decide

    [Fact]
    public void Critical_verified_by_non_independent_is_rejected_EX80()
    {
        var d = VerificationRules.Decide(
            VerificationResult.Verified, VulnerabilityStatus.PendingVerification, RiskLevel.Critical, isIndependent: false);

        Assert.False(d.Allowed);
        Assert.Equal(VerificationErrorCodes.IndependentVerificationRequired, d.Code);
        Assert.Null(d.NextStatus);
    }

    [Fact]
    public void Critical_verified_by_independent_resolves()
    {
        var d = VerificationRules.Decide(
            VerificationResult.Verified, VulnerabilityStatus.PendingVerification, RiskLevel.Critical, isIndependent: true);

        Assert.True(d.Allowed);
        Assert.Equal(VulnerabilityStatus.Resolved, d.NextStatus);
    }

    [Theory]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.Low)]
    [InlineData(null)]
    public void Non_critical_verified_does_not_need_independence(RiskLevel? level)
    {
        var d = VerificationRules.Decide(
            VerificationResult.Verified, VulnerabilityStatus.PendingVerification, level, isIndependent: false);

        Assert.True(d.Allowed);
        Assert.Equal(VulnerabilityStatus.Resolved, d.NextStatus);
    }

    [Theory]
    [InlineData(VerificationResult.NotResolved)]
    [InlineData(VerificationResult.PartiallyResolved)]
    public void Failed_result_on_critical_is_allowed_even_when_not_independent_and_reopens(VerificationResult result)
    {
        var d = VerificationRules.Decide(
            result, VulnerabilityStatus.PendingVerification, RiskLevel.Critical, isIndependent: false);

        Assert.True(d.Allowed);
        Assert.Equal(VulnerabilityStatus.Reopened, d.NextStatus);
    }

    [Theory]
    [InlineData(VulnerabilityStatus.InProgress)]
    [InlineData(VulnerabilityStatus.Resolved)]
    [InlineData(VulnerabilityStatus.Closed)]
    [InlineData(VulnerabilityStatus.Reopened)]
    public void Result_can_only_be_recorded_while_pending_verification(VulnerabilityStatus current)
    {
        var d = VerificationRules.Decide(VerificationResult.Verified, current, RiskLevel.Low, isIndependent: true);

        Assert.False(d.Allowed);
        Assert.Equal(VerificationErrorCodes.NotPendingVerification, d.Code);
    }

    // ------------------------------------------------------------ closure gate (BR-16)

    private static VerificationSnapshot Snap(VerificationResult r, bool independent, DateTime at) => new(r, independent, at);

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(VulnerabilityStatus.Resolved)]
    [InlineData(VulnerabilityStatus.Closed)]
    public void Critical_closure_needs_independent_verified_record(VulnerabilityStatus target)
    {
        Assert.False(VerificationRules.IsClosureAllowed(RiskLevel.Critical, target, [], null));
        Assert.False(VerificationRules.IsClosureAllowed(
            RiskLevel.Critical, target, [Snap(VerificationResult.Verified, false, T0)], null));
        Assert.False(VerificationRules.IsClosureAllowed(
            RiskLevel.Critical, target, [Snap(VerificationResult.NotResolved, true, T0)], null));
        Assert.True(VerificationRules.IsClosureAllowed(
            RiskLevel.Critical, target, [Snap(VerificationResult.Verified, true, T0)], null));
    }

    [Fact]
    public void Verification_before_last_reopen_does_not_count()
    {
        var reopenedAt = T0.AddDays(1);

        Assert.False(VerificationRules.IsClosureAllowed(
            RiskLevel.Critical, VulnerabilityStatus.Closed, [Snap(VerificationResult.Verified, true, T0)], reopenedAt));
        Assert.True(VerificationRules.IsClosureAllowed(
            RiskLevel.Critical, VulnerabilityStatus.Closed, [Snap(VerificationResult.Verified, true, reopenedAt.AddHours(1))], reopenedAt));
    }

    [Fact]
    public void Gate_does_not_apply_to_non_critical_or_other_targets()
    {
        Assert.True(VerificationRules.IsClosureAllowed(RiskLevel.High, VulnerabilityStatus.Closed, [], null));
        Assert.True(VerificationRules.IsClosureAllowed(RiskLevel.Critical, VulnerabilityStatus.Reopened, [], null));
        Assert.True(VerificationRules.IsClosureAllowed(RiskLevel.Critical, VulnerabilityStatus.PendingVerification, [], null));
    }

    // ------------------------------------------------------------ reopen on rescan (BR-13)

    [Theory]
    [InlineData(RescanStatus.StillDetected, VulnerabilityStatus.PendingVerification, true)]
    [InlineData(RescanStatus.StillDetected, VulnerabilityStatus.Resolved, true)]
    [InlineData(RescanStatus.StillDetected, VulnerabilityStatus.Closed, true)]
    [InlineData(RescanStatus.StillDetected, VulnerabilityStatus.InProgress, false)]
    [InlineData(RescanStatus.StillDetected, VulnerabilityStatus.Reopened, false)]
    [InlineData(RescanStatus.Resolved, VulnerabilityStatus.PendingVerification, false)]
    [InlineData(RescanStatus.Failed, VulnerabilityStatus.Resolved, false)]
    [InlineData(RescanStatus.TargetNotFound, VulnerabilityStatus.Closed, false)]
    public void Reopen_on_rescan_only_when_still_detected_after_fix(RescanStatus scan, VulnerabilityStatus current, bool expected)
        => Assert.Equal(expected, VerificationRules.ShouldReopenOnRescan(scan, current));

    [Theory]
    [InlineData(VulnerabilityStatus.PendingVerification, true)]
    [InlineData(VulnerabilityStatus.Resolved, true)]
    [InlineData(VulnerabilityStatus.Closed, true)]
    [InlineData(VulnerabilityStatus.New, false)]
    [InlineData(VulnerabilityStatus.InProgress, false)]
    [InlineData(VulnerabilityStatus.Reopened, false)]
    public void Rescan_can_be_requested_only_around_verification(VulnerabilityStatus current, bool expected)
        => Assert.Equal(expected, VerificationRules.CanRequestRescan(current));

    [Fact]
    public void Only_still_detected_rescan_produces_a_failed_verification_result()
    {
        Assert.Equal(VerificationResult.NotResolved, VerificationRules.ResultFromRescan(RescanStatus.StillDetected));
        Assert.Null(VerificationRules.ResultFromRescan(RescanStatus.Resolved));
        Assert.Null(VerificationRules.ResultFromRescan(RescanStatus.Failed));
        Assert.Null(VerificationRules.ResultFromRescan(RescanStatus.TargetNotFound));
    }

    // ------------------------------------------------------------ reopen on repeat finding (BR-13 / EX-46)

    [Fact]
    public void Recurrence_of_exact_match_reopens()
    {
        var id = Guid.NewGuid();
        var match = new DedupMatch(DedupMatchKind.Exact, id, [id], IsRecurrence: true);

        Assert.True(VerificationRules.ShouldReopenOnRecurrence(match));
    }

    [Fact]
    public void Exact_match_on_active_instance_does_not_reopen()
    {
        var id = Guid.NewGuid();

        Assert.False(VerificationRules.ShouldReopenOnRecurrence(new DedupMatch(DedupMatchKind.Exact, id, [id])));
    }

    [Fact]
    public void Ambiguous_or_new_match_never_reopens()
    {
        var id = Guid.NewGuid();

        Assert.False(VerificationRules.ShouldReopenOnRecurrence(new DedupMatch(DedupMatchKind.Ambiguous, null, [id], IsRecurrence: true)));
        Assert.False(VerificationRules.ShouldReopenOnRecurrence(DedupMatch.New));
    }

    [Fact]
    public void Exact_recurrence_without_instance_id_does_not_reopen()
        => Assert.False(VerificationRules.ShouldReopenOnRecurrence(new DedupMatch(DedupMatchKind.Exact, null, [], IsRecurrence: true)));

    [Fact]
    public void Dedup_matcher_flags_recurrence_only_for_resolved_or_closed_and_rule_follows()
    {
        var asset = Guid.NewGuid();
        var key = new DedupKey(asset, "CVE-2024-0001", "https://x/y", 443, null);

        DedupMatch Match(VulnerabilityStatus status)
            => DedupMatcher.Match(
                key, "Some vuln", [new DedupCandidate(Guid.NewGuid(), key, "Some vuln", status)]);

        Assert.True(VerificationRules.ShouldReopenOnRecurrence(Match(VulnerabilityStatus.Resolved)));
        Assert.True(VerificationRules.ShouldReopenOnRecurrence(Match(VulnerabilityStatus.Closed)));
        Assert.False(VerificationRules.ShouldReopenOnRecurrence(Match(VulnerabilityStatus.InProgress)));
        Assert.False(VerificationRules.ShouldReopenOnRecurrence(Match(VulnerabilityStatus.PendingVerification)));
    }

    [Fact]
    public void Reopen_reasons_are_non_empty_and_within_workflow_limit()
    {
        Assert.InRange(VerificationRules.RecurrenceReopenReason.Length, 1, 1000);
        Assert.InRange(VerificationRules.RescanReopenReason("MOCK-ABC").Length, 1, 1000);
        Assert.InRange(VerificationRules.VerificationReopenReason(VerificationResult.NotResolved).Length, 1, 1000);
    }
}
