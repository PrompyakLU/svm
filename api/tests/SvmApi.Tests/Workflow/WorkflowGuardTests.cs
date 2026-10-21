using SvmApi.Application.Workflow;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Tests.Workflow;

/// <summary>
/// WorkflowGuard (pure · C7 · US-019 · EX-55/56/57 · BR-13 · task 5.1/7.2):
/// transition ที่อนุญาต/ถูกบล็อก, ขาดหลักฐาน, ต้องอนุมัติ, ขาดสิทธิ์, rowVersion ล้าสมัย, reopen.
/// </summary>
public class WorkflowGuardTests
{
    private const string Transition = "instance.transition";
    private const string Approve = "instance.approve";
    private const string Record = "verification.record";

    private static TransitionRule Rule(
        VulnerabilityStatus from,
        VulnerabilityStatus to,
        string? permission = Transition,
        bool evidence = false,
        bool approval = false,
        bool active = true)
        => new(from, to, permission, evidence, approval, active);

    private static TransitionDecision Decide(
        TransitionRule? rule,
        VulnerabilityStatus from,
        VulnerabilityStatus to,
        string[] permissions,
        bool hasEvidence = false,
        bool hasApproval = false)
        => WorkflowGuard.CanTransition(from, to, rule, permissions, hasEvidence, hasApproval);

    // ------------------------------------------------------------ allowed

    [Fact]
    public void Allowed_when_rule_active_and_user_has_permission()
    {
        var rule = Rule(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview);

        var d = Decide(rule, VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, [Transition]);

        Assert.True(d.Allowed);
        Assert.Equal(WorkflowOutcome.Success, d.Outcome);
        Assert.Null(d.Code);
    }

    [Fact]
    public void Allowed_with_family_wildcard_permission()
    {
        var rule = Rule(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview);

        Assert.True(Decide(rule, VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, ["instance.*"]).Allowed);
    }

    [Fact]
    public void Allowed_when_evidence_and_approval_present()
    {
        var rule = Rule(VulnerabilityStatus.Confirmed, VulnerabilityStatus.FalsePositive, Approve, evidence: true, approval: true);

        var d = Decide(rule, VulnerabilityStatus.Confirmed, VulnerabilityStatus.FalsePositive, [Approve], hasEvidence: true, hasApproval: true);

        Assert.True(d.Allowed);
    }

    // ------------------------------------------------------------ EX-55 illegal transition

    [Fact]
    public void Blocked_when_no_rule_exists()
    {
        var d = Decide(null, VulnerabilityStatus.New, VulnerabilityStatus.Closed, [Transition]);

        Assert.False(d.Allowed);
        Assert.Equal(WorkflowOutcome.InvalidTransition, d.Outcome);
        Assert.Equal(WorkflowErrorCodes.InvalidTransition, d.Code);
    }

    [Fact]
    public void Blocked_when_rule_is_inactive()
    {
        var rule = Rule(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, active: false);

        var d = Decide(rule, VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, [Transition]);

        Assert.False(d.Allowed);
        Assert.Equal(WorkflowErrorCodes.InvalidTransition, d.Code);
    }

    [Fact]
    public void Blocked_when_rule_does_not_match_from_to()
    {
        var rule = Rule(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview);

        var d = Decide(rule, VulnerabilityStatus.Assigned, VulnerabilityStatus.UnderReview, [Transition]);

        Assert.Equal(WorkflowErrorCodes.InvalidTransition, d.Code);
    }

    [Fact]
    public void Blocked_when_target_equals_current()
    {
        var rule = Rule(VulnerabilityStatus.New, VulnerabilityStatus.New);

        var d = Decide(rule, VulnerabilityStatus.New, VulnerabilityStatus.New, [Transition]);

        Assert.Equal(WorkflowErrorCodes.InvalidTransition, d.Code);
    }

    [Fact]
    public void Invalid_transition_is_reported_before_permission()
    {
        // ไม่มีสิทธิ์ + ไม่มี rule → ต้องเป็น invalid transition ไม่ใช่ forbidden
        var d = Decide(null, VulnerabilityStatus.Archived, VulnerabilityStatus.New, []);

        Assert.Equal(WorkflowOutcome.InvalidTransition, d.Outcome);
    }

    // ------------------------------------------------------------ permission

    [Fact]
    public void Blocked_when_user_lacks_required_permission()
    {
        // Confirmed → Assigned ใช้ instance.assign; ผู้ใช้มีแค่ instance.transition
        var rule = Rule(VulnerabilityStatus.Confirmed, VulnerabilityStatus.Assigned, "instance.assign");

        var d = Decide(rule, VulnerabilityStatus.Confirmed, VulnerabilityStatus.Assigned, [Transition]);

        Assert.False(d.Allowed);
        Assert.Equal(WorkflowOutcome.Forbidden, d.Outcome);
        Assert.Equal(WorkflowErrorCodes.PermissionDenied, d.Code);
    }

    [Fact]
    public void Blocked_with_empty_permissions()
    {
        var rule = Rule(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview);

        Assert.Equal(WorkflowOutcome.Forbidden, Decide(rule, VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, []).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rule_without_permission_falls_back_to_instance_transition_fail_closed(string? permission)
    {
        var rule = Rule(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, permission);

        Assert.False(Decide(rule, VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, ["instance.read"]).Allowed);
        Assert.True(Decide(rule, VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, [Transition]).Allowed);
    }

    [Fact]
    public void Permission_is_checked_before_evidence_and_approval()
    {
        var rule = Rule(VulnerabilityStatus.Confirmed, VulnerabilityStatus.RiskAccepted, Approve, evidence: true, approval: true);

        var d = Decide(rule, VulnerabilityStatus.Confirmed, VulnerabilityStatus.RiskAccepted, [Transition]);

        Assert.Equal(WorkflowOutcome.Forbidden, d.Outcome);
    }

    // ------------------------------------------------------------ EX-56 evidence

    [Theory]
    [InlineData(VulnerabilityStatus.InProgress, VulnerabilityStatus.PendingVerification, "verification.submit")]
    [InlineData(VulnerabilityStatus.Resolved, VulnerabilityStatus.Closed, Record)]
    public void Blocked_when_evidence_required_but_missing(VulnerabilityStatus from, VulnerabilityStatus to, string permission)
    {
        var rule = Rule(from, to, permission, evidence: true);

        var d = Decide(rule, from, to, [permission], hasEvidence: false);

        Assert.False(d.Allowed);
        Assert.Equal(WorkflowOutcome.MissingEvidence, d.Outcome);
        Assert.Equal(WorkflowErrorCodes.MissingEvidence, d.Code);
    }

    [Fact]
    public void Allowed_when_evidence_required_and_present()
    {
        var rule = Rule(VulnerabilityStatus.InProgress, VulnerabilityStatus.PendingVerification, "verification.submit", evidence: true);

        var d = Decide(rule, VulnerabilityStatus.InProgress, VulnerabilityStatus.PendingVerification, ["verification.submit"], hasEvidence: true);

        Assert.True(d.Allowed);
    }

    [Fact]
    public void Evidence_flag_is_ignored_when_rule_does_not_require_it()
    {
        var rule = Rule(VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, evidence: false);

        Assert.True(Decide(rule, VulnerabilityStatus.New, VulnerabilityStatus.UnderReview, [Transition], hasEvidence: false).Allowed);
    }

    // ------------------------------------------------------------ approval

    [Fact]
    public void Blocked_when_approval_required_but_missing()
    {
        var rule = Rule(VulnerabilityStatus.Confirmed, VulnerabilityStatus.FalsePositive, Approve, evidence: true, approval: true);

        var d = Decide(rule, VulnerabilityStatus.Confirmed, VulnerabilityStatus.FalsePositive, [Approve], hasEvidence: true, hasApproval: false);

        Assert.False(d.Allowed);
        Assert.Equal(WorkflowOutcome.ApprovalRequired, d.Outcome);
        Assert.Equal(WorkflowErrorCodes.ApprovalRequired, d.Code);
    }

    [Fact]
    public void Missing_evidence_is_reported_before_missing_approval()
    {
        var rule = Rule(VulnerabilityStatus.Confirmed, VulnerabilityStatus.FalsePositive, Approve, evidence: true, approval: true);

        var d = Decide(rule, VulnerabilityStatus.Confirmed, VulnerabilityStatus.FalsePositive, [Approve], hasEvidence: false, hasApproval: false);

        Assert.Equal(WorkflowOutcome.MissingEvidence, d.Outcome);
    }

    // ------------------------------------------------------------ EX-57 rowVersion

    [Fact]
    public void RowVersion_equal_is_not_stale()
    {
        byte[] current = [0, 0, 0, 0, 0, 0, 0, 7];

        Assert.False(WorkflowGuard.IsStale([0, 0, 0, 0, 0, 0, 0, 7], current));
    }

    [Fact]
    public void RowVersion_different_is_stale()
    {
        byte[] current = [0, 0, 0, 0, 0, 0, 0, 8];

        Assert.True(WorkflowGuard.IsStale([0, 0, 0, 0, 0, 0, 0, 7], current));
    }

    [Fact]
    public void RowVersion_missing_or_wrong_length_is_stale_fail_closed()
    {
        byte[] current = [0, 0, 0, 0, 0, 0, 0, 7];

        Assert.True(WorkflowGuard.IsStale(null, current));
        Assert.True(WorkflowGuard.IsStale([7], current));
        Assert.True(WorkflowGuard.IsStale(current, null));
    }

    // ------------------------------------------------------------ reason

    [Theory]
    [InlineData(VulnerabilityStatus.FalsePositive, true)]
    [InlineData(VulnerabilityStatus.RiskAccepted, true)]
    [InlineData(VulnerabilityStatus.ExceptionApproved, true)]
    [InlineData(VulnerabilityStatus.Reopened, true)]
    [InlineData(VulnerabilityStatus.Confirmed, false)]
    [InlineData(VulnerabilityStatus.Closed, false)]
    public void Reason_required_only_for_exception_style_targets(VulnerabilityStatus to, bool expected)
        => Assert.Equal(expected, WorkflowGuard.ReasonRequired(to));

    // ------------------------------------------------------------ BR-13 reopen

    [Theory]
    [InlineData(VulnerabilityStatus.Resolved)]
    [InlineData(VulnerabilityStatus.Closed)]
    public void Reopen_allowed_from_resolved_or_closed_with_active_rule(VulnerabilityStatus current)
    {
        var rule = Rule(current, VulnerabilityStatus.Reopened);

        Assert.True(WorkflowGuard.CanReopen(current, rule).Allowed);
    }

    [Theory]
    [InlineData(VulnerabilityStatus.InProgress)]
    [InlineData(VulnerabilityStatus.Archived)]
    [InlineData(VulnerabilityStatus.FalsePositive)]
    public void Reopen_blocked_from_other_statuses(VulnerabilityStatus current)
    {
        var rule = Rule(current, VulnerabilityStatus.Reopened);

        var d = WorkflowGuard.CanReopen(current, rule);

        Assert.False(d.Allowed);
        Assert.Equal(WorkflowErrorCodes.InvalidTransition, d.Code);
    }

    [Fact]
    public void Reopen_blocked_when_rule_missing_or_inactive()
    {
        Assert.False(WorkflowGuard.CanReopen(VulnerabilityStatus.Closed, null).Allowed);
        Assert.False(WorkflowGuard.CanReopen(
            VulnerabilityStatus.Closed, Rule(VulnerabilityStatus.Closed, VulnerabilityStatus.Reopened, active: false)).Allowed);
    }
}
