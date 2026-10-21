using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Workflow;

/// <summary>คำขอเปลี่ยนสถานะ: <c>{ "toStatus": "Confirmed", "reason": "...", "rowVersion": "AAAA..." }</c>. rowVersion บังคับ (EX-57).</summary>
public sealed record TransitionRequest(string? ToStatus, string? Reason, string? RowVersion);

/// <summary>ผลการเปลี่ยนสถานะ (rowVersion ใหม่สำหรับ request ถัดไป).</summary>
public sealed record TransitionResultDto(
    Guid InstanceId,
    string FromStatus,
    string ToStatus,
    Guid HistoryId,
    DateTime ChangedAt,
    string RowVersion);

/// <summary>หนึ่งรายการของ <c>trans_statusHistory</c>.</summary>
public sealed record StatusHistoryDto(
    Guid Id,
    string FromStatus,
    string ToStatus,
    string? Reason,
    Guid ChangedBy,
    DateTime ChangedAt);

/// <summary>หนึ่งรายการของ <c>trans_riskScoreHistory</c> (ไม่รวม factor breakdown).</summary>
public sealed record RiskHistoryItemDto(
    Guid Id,
    decimal Score,
    string RiskLevel,
    string CalculationType,
    decimal? PreviousScore,
    string? Reason,
    Guid? CreatedBy,
    DateTime CreatedAt);

/// <summary>ประวัติของ instance: สถานะ + risk (ใหม่สุดก่อน).</summary>
public sealed record InstanceHistoryDto(
    Guid InstanceId,
    string CurrentStatus,
    IReadOnlyList<StatusHistoryDto> Status,
    IReadOnlyList<RiskHistoryItemDto> Risk);

/// <summary>ผลตรวจล่วงหน้าว่าเปลี่ยนสถานะได้หรือไม่ (ไม่เขียนข้อมูล).</summary>
public sealed record CanTransitionDto(bool Allowed, string? Code, string? Message);

public static class WorkflowErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InstanceNotFound = "INSTANCE_NOT_FOUND";

    /// <summary>EX-55.</summary>
    public const string InvalidTransition = "WORKFLOW_INVALID_TRANSITION";

    /// <summary>EX-56 · BR-14.</summary>
    public const string MissingEvidence = "MISSING_EVIDENCE";

    public const string ApprovalRequired = "APPROVAL_REQUIRED";

    /// <summary>BR-16 · EX-80.</summary>
    public const string IndependentVerificationRequired = "INDEPENDENT_VERIFICATION_REQUIRED";

    public const string PermissionDenied = "WORKFLOW_PERMISSION_DENIED";

    /// <summary>EX-57 (ใช้ code เดียวกับ Risk/Intake).</summary>
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";

    public const string ToStatusRequired = "TO_STATUS_REQUIRED";
    public const string InvalidStatus = "INVALID_STATUS";
    public const string ReasonRequired = "WORKFLOW_REASON_REQUIRED";
    public const string ReasonTooLong = "TOO_LONG";
    public const string RowVersionRequired = "ROW_VERSION_REQUIRED";
    public const string InvalidRowVersion = "INVALID_ROW_VERSION";
}

public enum WorkflowOutcome
{
    Success,
    NotFound,

    /// <summary>มีอยู่แต่อยู่นอก data scope (EX-07): ตอบ 404 + บันทึก denied-access.</summary>
    OutOfScope,

    ValidationFailed,

    /// <summary>EX-55 → 422.</summary>
    InvalidTransition,

    /// <summary>EX-56 → 422.</summary>
    MissingEvidence,

    /// <summary>ต้องมีการอนุมัติก่อน → 422.</summary>
    ApprovalRequired,

    /// <summary>ไม่มีสิทธิ์ตามแถว transition → 403.</summary>
    Forbidden,

    /// <summary>EX-57 → 409.</summary>
    Conflict,

    /// <summary>BR-16 / EX-80: Critical ต้องมี independent verification ก่อน Resolved/Closed → 422.</summary>
    IndependentVerificationRequired,

    /// <summary>ระบบภายนอก (scanner mock/adapter) ล้มเหลว เช่น EX-74 → 502 (ไม่เปลี่ยนสถานะ).</summary>
    ExternalServiceFailed,

    /// <summary>ทำไม่ได้ในสถานะ/ข้อมูลปัจจุบัน (เช่น EX-75 ไม่พบ target, บันทึกผลนอก Pending Verification) → 422 พร้อม code เฉพาะ.</summary>
    Unprocessable,
}

public sealed record WorkflowResult<T>(
    WorkflowOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Code = null,
    string? Message = null)
{
    public static WorkflowResult<T> Ok(T value) => new(WorkflowOutcome.Success, value);

    public static WorkflowResult<T> Fail(WorkflowOutcome outcome, string code, string? message = null)
        => new(outcome, default, null, code, message);

    public static WorkflowResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors, string code = WorkflowErrorCodes.ValidationError)
        => new(WorkflowOutcome.ValidationFailed, default, errors, code);
}

/// <summary>
/// ตรวจว่า instance มีหลักฐานครบสำหรับ transition ที่บังคับ evidence (EX-56, BR-14).
/// Default implementation อ่าน <c>trans_evidence</c> (เฉพาะไฟล์ที่ scan = Clean — NFR-18); task 5.2 ใช้ซ้ำได้.
/// </summary>
public interface IEvidenceChecker
{
    Task<bool> HasRequiredEvidenceAsync(Guid instanceId, CancellationToken cancellationToken);
}

/// <summary>
/// ตรวจว่า instance มีการอนุมัติแล้วสำหรับ transition ที่ <c>requiresApproval</c>.
/// Default = ปฏิเสธเสมอ (fail-closed) จนกว่าจะมี approval module; แทนที่ได้โดยลงทะเบียน implementation ใหม่.
/// </summary>
public interface IApprovalChecker
{
    Task<bool> HasApprovalAsync(Guid instanceId, VulnerabilityStatus targetStatus, CancellationToken cancellationToken);
}

/// <summary>
/// WorkflowModule (C7 · US-018/019 · FR-22/23 · BR-27/13): state machine แบบ table-driven จาก <c>config_statusTransition</c>.
/// ทุกการเปลี่ยนสถานะเขียน <c>trans_statusHistory</c> + audit ใน transaction เดียว; instance ถูกกรองด้วย data scope.
/// </summary>
public interface IWorkflowService
{
    /// <summary>ตรวจล่วงหน้าว่าผู้ใช้ปัจจุบันเปลี่ยนเป็น <paramref name="toStatus"/> ได้หรือไม่ (ไม่เขียนข้อมูล · ไม่ตรวจ rowVersion).</summary>
    Task<WorkflowResult<CanTransitionDto>> CanTransitionAsync(Guid instanceId, VulnerabilityStatus toStatus, CancellationToken cancellationToken);

    /// <summary>เปลี่ยนสถานะพร้อม guard (permission/evidence/approval) และ optimistic concurrency (EX-55/56/57).</summary>
    Task<WorkflowResult<TransitionResultDto>> TransitionAsync(Guid instanceId, TransitionRequest request, CancellationToken cancellationToken);

    /// <summary>ประวัติสถานะ + risk ของ instance (ใหม่สุดก่อน).</summary>
    Task<WorkflowResult<InstanceHistoryDto>> GetHistoryAsync(Guid instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// BR-13: reopen instance ที่ Resolved/Closed เมื่อพบซ้ำ หรือ verify ไม่ผ่าน — เรียกโดย Intake/Verification (system action).
    /// ข้ามการตรวจ permission/evidence/approval ของผู้ใช้ แต่ยังต้องมีแถว transition ที่ active (เช่น Resolved→Reopened) และเขียน history + audit.
    /// ถ้า instance ไม่ได้อยู่ใน Resolved/Closed จะไม่ทำอะไร (คืน InvalidTransition).
    /// </summary>
    Task<WorkflowResult<TransitionResultDto>> ReopenAsync(Guid instanceId, string reason, CancellationToken cancellationToken);
}
