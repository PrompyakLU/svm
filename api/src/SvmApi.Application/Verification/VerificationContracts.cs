using SvmApi.Application.Workflow;

namespace SvmApi.Application.Verification;

/// <summary>รหัส error ของ VerificationModule (C10 · US-027/028/029 · EX-74..80).</summary>
public static class VerificationErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string ResultRequired = "VERIFICATION_RESULT_REQUIRED";
    public const string InvalidResult = "VERIFICATION_INVALID_RESULT";
    public const string InvalidMethod = "VERIFICATION_INVALID_METHOD";
    public const string NotesTooLong = "TOO_LONG";
    public const string RowVersionRequired = "ROW_VERSION_REQUIRED";

    /// <summary>บันทึกผลได้เฉพาะ instance ที่อยู่ใน Pending Verification.</summary>
    public const string NotPendingVerification = "VERIFICATION_NOT_PENDING";

    /// <summary>BR-16 · EX-80.</summary>
    public const string IndependentVerificationRequired = WorkflowErrorCodes.IndependentVerificationRequired;

    /// <summary>ขอ rescan ได้เฉพาะ Pending Verification/Resolved/Closed.</summary>
    public const string RescanNotAllowed = "RESCAN_NOT_ALLOWED";

    /// <summary>EX-74: ส่งคำขอ rescan ไม่สำเร็จ.</summary>
    public const string RescanFailed = "RESCAN_FAILED";

    /// <summary>EX-75: scanner ไม่พบ target.</summary>
    public const string RescanTargetNotFound = "RESCAN_TARGET_NOT_FOUND";
}

public static class VerificationLimits
{
    public const int MaxNotesLength = 2000;
}

/// <summary>ผลการ rescan จาก scanner adapter (mock ในช่วง workshop).</summary>
public enum RescanStatus
{
    /// <summary>ไม่พบช่องโหว่เดิมแล้ว.</summary>
    Resolved,

    /// <summary>ยังพบช่องโหว่เดิม.</summary>
    StillDetected,

    /// <summary>EX-75: ไม่พบ target (asset/location).</summary>
    TargetNotFound,

    /// <summary>EX-74: scanner ใช้ไม่ได้/ส่งคำขอไม่สำเร็จ.</summary>
    Failed,
}

/// <summary>คำขอ rescan ที่ส่งให้ scanner (<c>RequestRescan(assetId, instanceRef)</c> ใน integration.md). ไม่มีข้อมูลส่วนบุคคล.</summary>
public sealed record RescanRequest(
    Guid AssetId,
    string AssetCode,
    Guid InstanceId,
    string? CveId,
    string? Location,
    int? Port,
    string? Component);

public sealed record RescanResponse(string RequestId, RescanStatus Status);

/// <summary><c>POST /instances/{id}/submit-verification</c>: <c>{ "rowVersion": "AAAA...", "notes": "..." }</c> (rowVersion บังคับ · EX-57).</summary>
public sealed record SubmitVerificationRequest(string? RowVersion, string? Notes);

/// <summary><c>POST /instances/{id}/verification</c>: <c>{ "result": "Verified", "method": "Rescan", "notes": "...", "rowVersion": "AAAA..." }</c>.</summary>
public sealed record RecordVerificationRequest(string? Result, string? Method, string? Notes, string? RowVersion);

/// <summary>ผล <c>POST /instances/{id}/verification</c> (rowVersion ใหม่สำหรับ request ถัดไป).</summary>
public sealed record VerificationRecordedDto(
    Guid VerificationId,
    Guid InstanceId,
    string Result,
    string Method,
    bool IsIndependent,
    string FromStatus,
    string ToStatus,
    string RowVersion);

/// <summary>
/// ผล <c>POST /instances/{id}/rescan</c>. <c>Status</c> = <c>Resolved</c> (ไม่พบแล้ว · ไม่เปลี่ยนสถานะ — ผู้ตรวจต้องบันทึกผลเอง)
/// หรือ <c>StillDetected</c> (พบซ้ำ → <c>Reopened = true</c> ถ้าเปลี่ยนสถานะสำเร็จ · BR-13).
/// </summary>
public sealed record RescanResultDto(
    string RequestId,
    string Status,
    bool Reopened,
    Guid? VerificationId,
    string FromStatus,
    string ToStatus,
    string RowVersion);

/// <summary>
/// VerificationModule (C10 · US-027/028/029 · FR-43..47): ส่งตรวจ, rescan (mock), บันทึกผล, ปิด/reopen ผ่าน <see cref="IWorkflowService"/> เท่านั้น.
/// บันทึกผล + เปลี่ยนสถานะ + audit อยู่ใน transaction เดียว; instance กรองด้วย data scope (นอก scope = OutOfScope → 404).
/// </summary>
public interface IVerificationService
{
    /// <summary>InProgress → PendingVerification (ต้องมีหลักฐาน Clean · BR-14/EX-56) ผ่าน workflow.</summary>
    Task<WorkflowResult<TransitionResultDto>> SubmitAsync(Guid instanceId, SubmitVerificationRequest request, CancellationToken cancellationToken);

    /// <summary>ขอ rescan ผ่าน scanner adapter; พบซ้ำ → บันทึก NotResolved + Reopen (BR-13). ล้มเหลว/ไม่พบ target = EX-74/75 (แจ้งผู้ขอ).</summary>
    Task<WorkflowResult<RescanResultDto>> RescanAsync(Guid instanceId, CancellationToken cancellationToken);

    /// <summary>บันทึกผล Verified/NotResolved/PartiallyResolved แล้วเลื่อนสถานะตาม <see cref="VerificationRules.NextStatus"/> (BR-16 สำหรับ Critical).</summary>
    Task<WorkflowResult<VerificationRecordedDto>> RecordAsync(Guid instanceId, RecordVerificationRequest request, CancellationToken cancellationToken);
}
