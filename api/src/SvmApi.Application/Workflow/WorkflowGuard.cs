using SvmApi.Application.Auth;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Workflow;

/// <summary>แถวหนึ่งของ <c>config_statusTransition</c> ที่อ่านจาก DB ตอน runtime (table-driven — KD#2, NFR-31).</summary>
public sealed record TransitionRule(
    VulnerabilityStatus From,
    VulnerabilityStatus To,
    string? RequiredPermission,
    bool RequiresEvidence,
    bool RequiresApproval,
    bool IsActive = true);

/// <summary>ผลตัดสินของ guard: อนุญาต หรือ ปฏิเสธพร้อม <see cref="WorkflowOutcome"/> + code.</summary>
public sealed record TransitionDecision(bool Allowed, WorkflowOutcome Outcome, string? Code, string? Message)
{
    public static TransitionDecision Allow { get; } = new(true, WorkflowOutcome.Success, null, null);

    public static TransitionDecision Deny(WorkflowOutcome outcome, string code, string message)
        => new(false, outcome, code, message);
}

/// <summary>
/// guard ของ state machine (pure · ไม่แตะ DB · unit-testable) — US-019 · FR-23 · BR-27 · EX-55/56/57.
/// ลำดับตรวจ: มี transition ที่ active → สิทธิ์ → หลักฐาน → การอนุมัติ
/// (ตรวจสิทธิ์ก่อนหลักฐาน/อนุมัติ เพื่อไม่เปิดเผยสถานะของข้อมูลให้ผู้ไม่มีสิทธิ์).
/// </summary>
public static class WorkflowGuard
{
    /// <summary>permission ที่ใช้เมื่อแถว transition ไม่ได้ระบุ requiredPermission (fail-closed).</summary>
    public const string DefaultPermission = "instance.transition";

    /// <summary>สถานะปลายทางที่ต้องมีเหตุผลประกอบ (ปฏิเสธ/ยอมรับความเสี่ยง/เปิดกลับ).</summary>
    public static bool ReasonRequired(VulnerabilityStatus to)
        => to is VulnerabilityStatus.FalsePositive
            or VulnerabilityStatus.RiskAccepted
            or VulnerabilityStatus.ExceptionApproved
            or VulnerabilityStatus.Reopened;

    /// <summary>
    /// ตัดสินว่าเปลี่ยนจาก <paramref name="current"/> เป็น <paramref name="target"/> ได้หรือไม่.
    /// <paramref name="hasEvidence"/>/<paramref name="hasApproval"/> ถูกอ่านเฉพาะเมื่อ rule บังคับ
    /// (ผู้เรียกจึงไม่ต้อง query เมื่อ rule ไม่ต้องการ).
    /// </summary>
    public static TransitionDecision CanTransition(
        VulnerabilityStatus current,
        VulnerabilityStatus target,
        TransitionRule? rule,
        IEnumerable<string> userPermissions,
        bool hasEvidence,
        bool hasApproval)
    {
        ArgumentNullException.ThrowIfNull(userPermissions);

        // EX-55: ไม่มีแถว / ไม่ active / ไม่ตรง from→to / เปลี่ยนเป็นสถานะเดิม
        if (rule is null || !rule.IsActive || current == target || rule.From != current || rule.To != target)
        {
            return TransitionDecision.Deny(
                WorkflowOutcome.InvalidTransition, WorkflowErrorCodes.InvalidTransition,
                $"Transition from '{current}' to '{target}' is not allowed.");
        }

        var required = string.IsNullOrWhiteSpace(rule.RequiredPermission) ? DefaultPermission : rule.RequiredPermission.Trim();
        if (!PermissionMatcher.HasPermission(userPermissions, required))
        {
            return TransitionDecision.Deny(
                WorkflowOutcome.Forbidden, WorkflowErrorCodes.PermissionDenied,
                "You do not have permission to perform this status transition.");
        }

        // EX-56 / BR-14
        if (rule.RequiresEvidence && !hasEvidence)
        {
            return TransitionDecision.Deny(
                WorkflowOutcome.MissingEvidence, WorkflowErrorCodes.MissingEvidence,
                "Evidence is required before this status transition.");
        }

        if (rule.RequiresApproval && !hasApproval)
        {
            return TransitionDecision.Deny(
                WorkflowOutcome.ApprovalRequired, WorkflowErrorCodes.ApprovalRequired,
                "An approval is required before this status transition.");
        }

        return TransitionDecision.Allow;
    }

    /// <summary>
    /// BR-13 (system reopen): อนุญาตเฉพาะ instance ที่ Resolved/Closed และมีแถว transition → Reopened ที่ active.
    /// ไม่ตรวจ permission/evidence/approval ของผู้ใช้ (เป็น action ของระบบเมื่อพบซ้ำ/verify ไม่ผ่าน).
    /// </summary>
    public static TransitionDecision CanReopen(VulnerabilityStatus current, TransitionRule? rule)
    {
        if (current is not (VulnerabilityStatus.Resolved or VulnerabilityStatus.Closed)
            || rule is null || !rule.IsActive
            || rule.From != current || rule.To != VulnerabilityStatus.Reopened)
        {
            return TransitionDecision.Deny(
                WorkflowOutcome.InvalidTransition, WorkflowErrorCodes.InvalidTransition,
                $"Transition from '{current}' to '{VulnerabilityStatus.Reopened}' is not allowed.");
        }

        return TransitionDecision.Allow;
    }

    /// <summary>
    /// EX-57: rowVersion ที่ผู้ใช้ถือมาต้องตรงกับค่าปัจจุบัน. คืน true = stale (ต้องตอบ 409).
    /// ค่าที่ไม่มี/ความยาวไม่ตรงถือว่า stale (fail-closed).
    /// </summary>
    public static bool IsStale(byte[]? expected, byte[]? current)
        => expected is null || current is null || !expected.AsSpan().SequenceEqual(current);
}
