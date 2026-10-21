using SvmApi.Application.Intake;
using SvmApi.Domain.Verification;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Verification;

/// <summary>ผลตัดสินการบันทึก verification (pure).</summary>
public sealed record VerificationDecision(
    bool Allowed,
    string? Code,
    string? Message,
    VulnerabilityStatus? NextStatus)
{
    public static VerificationDecision Allow(VulnerabilityStatus next) => new(true, null, null, next);

    public static VerificationDecision Deny(string code, string message) => new(false, code, message, null);
}

/// <summary>ข้อมูลที่ต้องใช้จาก <c>trans_verificationRecord</c> เพื่อตัดสินการปิด (ไม่ผูก entity).</summary>
public sealed record VerificationSnapshot(VerificationResult Result, bool IsIndependent, DateTime VerifiedAt);

/// <summary>
/// กติกา pure ของ VerificationModule (C10 · US-027/028/029 · FR-43..47 · BR-13/15/16 · EX-76..80): ไม่แตะ DB — unit-testable.
/// <list type="bullet">
/// <item>Critical ต้อง verify โดยผู้ที่ไม่ใช่ผู้แก้ไข/ผู้ส่งงาน (independent) จึงผ่าน (BR-16, EX-80).</item>
/// <item>ผล → สถานะถัดไป: Verified → Resolved; NotResolved/PartiallyResolved → Reopened (ไม่ปิด · EX-76..78).</item>
/// <item>พบช่องโหว่ซ้ำ (rescan หรือ import) หลังส่งตรวจ/แก้แล้ว → Reopened (BR-13).</item>
/// </list>
/// </summary>
public static class VerificationRules
{
    /// <summary>BR-16: ระดับที่ต้องมี independent verifier (เฉพาะ Critical; ไม่มี risk level = ไม่บังคับ).</summary>
    public static bool RequiresIndependentVerifier(RiskLevel? level) => level == RiskLevel.Critical;

    /// <summary>
    /// ผู้ verify เป็น independent เมื่อไม่ตรงกับผู้รับผิดชอบการแก้ไขคนใดเลย (assignee, เจ้าของ task, ผู้ส่งเข้า Pending Verification).
    /// <see cref="Guid.Empty"/>/null ในรายการถูกข้าม; verifier ที่เป็น <see cref="Guid.Empty"/> ไม่ถือว่า independent (fail-closed).
    /// </summary>
    public static bool IsIndependent(Guid verifierId, IEnumerable<Guid?> responsibleUserIds)
    {
        ArgumentNullException.ThrowIfNull(responsibleUserIds);

        if (verifierId == Guid.Empty)
        {
            return false;
        }

        return responsibleUserIds.All(id => id is null || id == Guid.Empty || id.Value != verifierId);
    }

    /// <summary>ผล → สถานะถัดไปของ workflow (ตารางเดียว ใช้ทั้ง service และ test).</summary>
    public static VulnerabilityStatus NextStatus(VerificationResult result) => result switch
    {
        VerificationResult.Verified => VulnerabilityStatus.Resolved,
        VerificationResult.NotResolved => VulnerabilityStatus.Reopened,
        VerificationResult.PartiallyResolved => VulnerabilityStatus.Reopened,
        _ => VulnerabilityStatus.Reopened, // fail-safe: ค่าไม่รู้จัก = ไม่ปิด
    };

    /// <summary>
    /// ตัดสินการบันทึกผล: instance ต้องอยู่ที่ <see cref="VulnerabilityStatus.PendingVerification"/> (US-027);
    /// ผล Verified บน Critical ต้อง independent (BR-16/EX-80). ผลไม่ผ่านบันทึกได้เสมอ (ไม่ใช่การปิด).
    /// </summary>
    public static VerificationDecision Decide(
        VerificationResult result, VulnerabilityStatus current, RiskLevel? riskLevel, bool isIndependent)
    {
        if (current != VulnerabilityStatus.PendingVerification)
        {
            return VerificationDecision.Deny(
                VerificationErrorCodes.NotPendingVerification,
                $"Verification can only be recorded while the instance is '{VulnerabilityStatus.PendingVerification}'.");
        }

        if (result == VerificationResult.Verified && RequiresIndependentVerifier(riskLevel) && !isIndependent)
        {
            return VerificationDecision.Deny(
                VerificationErrorCodes.IndependentVerificationRequired,
                "Critical findings must be verified by someone other than the remediation owner.");
        }

        return VerificationDecision.Allow(NextStatus(result));
    }

    /// <summary>
    /// BR-16 ตอนปิด: Critical ที่จะเป็น Resolved/Closed ต้องมี record Verified+independent ที่เกิดหลัง <paramref name="sinceUtc"/>
    /// (เวลา reopen ล่าสุด — ผล verify ก่อนรอบ reopen ใช้ไม่ได้). ระดับอื่นไม่ถูกบังคับ.
    /// </summary>
    public static bool IsClosureAllowed(
        RiskLevel? riskLevel,
        VulnerabilityStatus target,
        IEnumerable<VerificationSnapshot> records,
        DateTime? sinceUtc)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (!RequiresIndependentVerifier(riskLevel) || target is not (VulnerabilityStatus.Resolved or VulnerabilityStatus.Closed))
        {
            return true;
        }

        return records.Any(r =>
            r.Result == VerificationResult.Verified
            && r.IsIndependent
            && (sinceUtc is null || r.VerifiedAt >= sinceUtc.Value));
    }

    /// <summary>ผล rescan → ผล verification ที่บันทึก (เฉพาะกรณีพบซ้ำ — ไม่พบ ≠ verified จนกว่าผู้ตรวจจะรับรอง).</summary>
    public static VerificationResult? ResultFromRescan(RescanStatus status)
        => status == RescanStatus.StillDetected ? VerificationResult.NotResolved : null;

    /// <summary>BR-13: rescan ยังพบช่องโหว่ และ instance อยู่ในสถานะที่ถือว่า "แก้แล้ว/รอตรวจ" → ต้อง reopen.</summary>
    public static bool ShouldReopenOnRescan(RescanStatus status, VulnerabilityStatus current)
        => status == RescanStatus.StillDetected
           && current is VulnerabilityStatus.PendingVerification or VulnerabilityStatus.Resolved or VulnerabilityStatus.Closed;

    /// <summary>rescan ขอได้เฉพาะ instance ที่อยู่ระหว่างตรวจ หรือแก้/ปิดแล้ว (เพื่อตรวจซ้ำ).</summary>
    public static bool CanRequestRescan(VulnerabilityStatus current)
        => current is VulnerabilityStatus.PendingVerification or VulnerabilityStatus.Resolved or VulnerabilityStatus.Closed;

    /// <summary>
    /// BR-13 / EX-46: finding ใหม่ที่ dedup ตรงกับ instance ที่ Resolved/Closed (recurrence) → ต้อง reopen.
    /// ไม่ reopen เมื่อ match ไม่ชัด (Ambiguous/New) หรือไม่มี instance.
    /// </summary>
    public static bool ShouldReopenOnRecurrence(DedupMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.Kind == DedupMatchKind.Exact && match.IsRecurrence && match.InstanceId is { } id && id != Guid.Empty;
    }

    /// <summary>เหตุผลมาตรฐานของ system reopen (ไม่มีข้อมูลส่วนบุคคล).</summary>
    public const string RecurrenceReopenReason = "Finding detected again (recurrence) — reopened automatically (BR-13).";

    public static string RescanReopenReason(string requestId)
        => $"Rescan still detects the vulnerability (requestId={requestId}) — reopened (BR-13).";

    public static string VerificationReopenReason(VerificationResult result)
        => $"Verification result '{result}' — reopened (BR-13).";
}
