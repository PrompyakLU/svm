using SvmApi.Domain.Remediation;

namespace SvmApi.Application.Remediation;

// ====================================================================== error codes

public static class RemediationErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InstanceNotFound = "INSTANCE_NOT_FOUND";
    public const string TaskNotFound = "TASK_NOT_FOUND";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";

    // --- assignment (EX-59..62)
    public const string AssigneeRequired = "ASSIGNEE_REQUIRED";
    public const string AssigneeConflict = "ASSIGNEE_CONFLICT";
    public const string AssigneeNotFound = "ASSIGNEE_NOT_FOUND";
    public const string AssigneeInactive = "ASSIGNEE_INACTIVE";
    public const string AssigneeOutOfScope = "ASSIGNEE_OUT_OF_SCOPE";
    public const string ReasonRequired = "ASSIGNMENT_REASON_REQUIRED";
    public const string InvalidStatusForAssignment = "ASSIGNMENT_INVALID_STATUS";
    public const string NoAssignmentRule = "ASSIGNMENT_NO_RULE_MATCH";
    public const string TeamTooLong = "TEAM_TOO_LONG";
    public const string BulkEmpty = "BULK_EMPTY";
    public const string BulkTooLarge = "BULK_TOO_LARGE";
    public const string OutOfScope = "OUT_OF_SCOPE";

    // --- task
    public const string NotTaskOwner = "TASK_NOT_OWNER";
    public const string NoChanges = "NO_CHANGES";
    public const string InvalidProgress = "INVALID_PROGRESS";
    public const string InvalidTaskStatus = "INVALID_STATUS";
    public const string ProgressMismatch = "PROGRESS_STATUS_MISMATCH";
    public const string ObstacleRequired = "OBSTACLE_REQUIRED";
    public const string TooLong = "TOO_LONG";
    public const string RowVersionRequired = "ROW_VERSION_REQUIRED";
    public const string InvalidRowVersion = "INVALID_ROW_VERSION";

    // --- evidence (EX-21/22)
    public const string FileMissing = "EVIDENCE_FILE_MISSING";
    public const string FileEmpty = "EVIDENCE_FILE_EMPTY";
    public const string FileTooLarge = "EVIDENCE_FILE_TOO_LARGE";
    public const string InvalidFileName = "EVIDENCE_INVALID_FILE_NAME";
    public const string TypeNotAllowed = "EVIDENCE_TYPE_NOT_ALLOWED";
    public const string ContentMismatch = "EVIDENCE_CONTENT_MISMATCH";
    public const string Malware = "EVIDENCE_MALWARE";
    public const string Unscannable = "EVIDENCE_UNSCANNABLE";
    public const string StorageFailed = "EVIDENCE_STORAGE_FAILED";
    public const string TaskMismatch = "EVIDENCE_TASK_MISMATCH";

    // --- comment
    public const string BodyRequired = "COMMENT_BODY_REQUIRED";
    public const string BodyTooLong = "COMMENT_BODY_TOO_LONG";
}

// ====================================================================== result

public enum RemediationOutcome
{
    Success,
    NotFound,

    /// <summary>มีอยู่แต่นอก data scope (EX-07): ตอบ 404 + บันทึก denied-access.</summary>
    OutOfScope,

    ValidationFailed,

    /// <summary>403 — ไม่ใช่เจ้าของงาน ฯลฯ.</summary>
    Forbidden,

    /// <summary>409 — rowVersion ไม่ตรง/ชนกัน.</summary>
    Conflict,

    /// <summary>422 — ข้อมูลถูกรูปแบบแต่ทำไม่ได้ (assignee ไม่ถูกต้อง, malware, สถานะไม่รองรับ).</summary>
    Unprocessable,

    /// <summary>413.</summary>
    PayloadTooLarge,

    /// <summary>415 — ชนิดไฟล์ไม่อนุญาต / เนื้อหาไม่ตรงนามสกุล.</summary>
    UnsupportedMediaType,
}

public sealed record RemediationResult<T>(
    RemediationOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Code = null,
    string? Message = null)
{
    public static RemediationResult<T> Ok(T value) => new(RemediationOutcome.Success, value);

    public static RemediationResult<T> Fail(RemediationOutcome outcome, string code, string? message = null)
        => new(outcome, default, null, code, message);

    public static RemediationResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors, string code = RemediationErrorCodes.ValidationError)
        => new(RemediationOutcome.ValidationFailed, default, errors, code);
}

// ====================================================================== assignment DTOs

/// <summary>
/// <c>{ "assignToUserId": "...", "assignToTeam": "...", "reason": "...", "rowVersion": "AAAA..." }</c>.
/// ไม่ส่ง assignee ทั้งคู่ = มอบหมายอัตโนมัติตาม <c>config_assignmentRule</c> (FR-24).
/// reason บังคับเมื่อเป็นการเปลี่ยนผู้รับผิดชอบเดิม (FR-25); rowVersion ไม่บังคับ แต่ถ้าส่งมาต้องตรง (EX-57).
/// </summary>
public sealed record AssignRequest(Guid? AssignToUserId, string? AssignToTeam, string? Reason, string? RowVersion);

/// <summary>Status = <c>Assigned</c> หรือ <c>Queued</c> (ไม่พบกฎ/assignee ใช้ไม่ได้ → คงค้างในคิว unassigned และแจ้งผู้ทำรายการ — EX-59/60).</summary>
public sealed record AssignResultDto(
    Guid InstanceId,
    string Status,
    Guid? AssignedToUserId,
    string? AssignedToTeam,
    Guid? TaskId,
    bool AutoAssigned,
    string? Code,
    string RowVersion);

/// <summary><c>{ "instanceIds": [...], "assignToUserId": "...", "assignToTeam": "...", "reason": "..." }</c> (EX-61).</summary>
public sealed record BulkAssignRequest(IReadOnlyList<Guid>? InstanceIds, Guid? AssignToUserId, string? AssignToTeam, string? Reason);

public sealed record BulkSkippedDto(Guid Id, string Reason);

public sealed record BulkAssignResultDto(IReadOnlyList<Guid> Assigned, IReadOnlyList<BulkSkippedDto> Skipped);

// ====================================================================== task DTOs

/// <summary>
/// อัปเดตแผน/ความคืบหน้า (partial: ฟิลด์ที่เป็น null = ไม่เปลี่ยน; สตริงว่าง = ล้างค่า). rowVersion บังคับ (จาก GET).
/// <c>comment</c> (ถ้ามี) บันทึกเป็น comment บน instance (FR-29).
/// </summary>
public sealed record UpdateTaskRequest(
    string? RootCause,
    string? ActionPlan,
    DateTime? TargetDate,
    string? ChangeReference,
    int? ProgressPercent,
    string? Status,
    string? Obstacle,
    string? Comment,
    string? RowVersion);

public sealed record RemediationTaskDto(
    Guid Id,
    Guid InstanceId,
    Guid? OwnerUserId,
    string? OwnerTeam,
    DateTime DueDate,
    string RiskLevel,
    string Recommendation,
    string? RootCause,
    string? ActionPlan,
    DateTime? TargetDate,
    string? ChangeReference,
    int ProgressPercent,
    string Status,
    string? Obstacle,
    bool TargetDateExceedsSla,
    string RowVersion,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// ====================================================================== evidence

/// <summary>ข้อจำกัดการอัปโหลด evidence (bind จาก section <c>Evidence</c>). ค่าเกิน hard cap ถูก clamp.</summary>
public sealed class EvidenceOptions
{
    public const string SectionName = "Evidence";

    /// <summary>เพดานขนาด request ของ endpoint upload — 25 MB.</summary>
    public const long HardMaxFileBytes = 25L * 1024 * 1024;

    /// <summary>ขนาดไฟล์สูงสุด (byte) — default 10 MB.</summary>
    public long MaxFileBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>
    /// โฟลเดอร์เก็บไฟล์ของ LocalFileStorage — ต้องอยู่นอก web root. ว่าง = <c>%LOCALAPPDATA%/svm/evidence</c>
    /// (หรือ <c>~/.local/share/svm/evidence</c>); production ควรตั้ง path เฉพาะ + ACL จำกัด.
    /// </summary>
    public string? StorageRoot { get; set; }

    public long EffectiveMaxFileBytes => Math.Clamp(MaxFileBytes, 1, HardMaxFileBytes);
}

/// <summary>คำขออัปโหลด: ชื่อไฟล์ดั้งเดิม (ใช้แสดงผลหลัง sanitize เท่านั้น) + เนื้อหา; Content-Type ของ client ไม่ถูกใช้.</summary>
public sealed record EvidenceUploadRequest(string? FileName, byte[]? Content, Guid? TaskId);

public sealed record EvidenceDto(
    Guid Id,
    Guid InstanceId,
    Guid? TaskId,
    int Version,
    string FileName,
    string ContentType,
    long FileSize,
    string ScanStatus,
    string? Sha256,
    Guid UploadedBy,
    DateTime UploadedAt);

/// <summary>ที่เก็บไฟล์ evidence (mock/local). key เป็น relative path ที่ service สร้างเอง — ห้ามมีชื่อไฟล์ผู้ใช้.</summary>
public interface IFileStorage
{
    /// <summary>เขียนไฟล์ใหม่ (ห้ามเขียนทับ — โยน <see cref="IOException"/> ถ้ามีอยู่แล้ว). key ที่ไม่ปลอดภัย → <see cref="ArgumentException"/>.</summary>
    Task SaveAsync(string storageKey, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>ลบไฟล์ (ใช้ชดเชยเมื่อบันทึก DB ล้มเหลว); ไม่มีไฟล์ = ไม่ทำอะไร.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

/// <summary>สแกน malware (mock · NFR-18). ผลคือ Clean / Infected / Unscannable เท่านั้น (ไม่คืน Pending).</summary>
public interface IMalwareScanner
{
    Task<ScanStatus> ScanAsync(ReadOnlyMemory<byte> content, string extension, CancellationToken cancellationToken);
}

// ====================================================================== comment

/// <summary><c>{ "body": "ดู @officer1 ด้วย" }</c> — mention ดึงจาก <c>@username</c> ในข้อความ.</summary>
public sealed record AddCommentRequest(string? Body);

public sealed record MentionDto(Guid UserId, string Username);

public sealed record CommentDto(
    Guid Id,
    Guid InstanceId,
    string Body,
    IReadOnlyList<MentionDto> Mentions,
    Guid CreatedBy,
    DateTime CreatedAt);

// ====================================================================== services

/// <summary>assign / bulk-assign (C8 · US-020 · FR-24/25/26 · BR-06/07 · EX-59..62).</summary>
public interface IAssignmentService
{
    Task<RemediationResult<AssignResultDto>> AssignAsync(Guid instanceId, AssignRequest request, CancellationToken cancellationToken);

    Task<RemediationResult<BulkAssignResultDto>> BulkAssignAsync(BulkAssignRequest request, CancellationToken cancellationToken);
}

/// <summary>remediation task / plan / progress (C8 · US-021 · FR-27/28/29 · EX-63).</summary>
public interface IRemediationTaskService
{
    Task<RemediationResult<RemediationTaskDto>> GetAsync(Guid taskId, CancellationToken cancellationToken);

    Task<RemediationResult<RemediationTaskDto>> UpdateAsync(Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken);
}

/// <summary>evidence แบบมี version + malware scan ก่อนรับ (C8 · US-022 · FR-31 · BR-42 · NFR-18 · EX-21/22).</summary>
public interface IEvidenceService
{
    Task<RemediationResult<EvidenceDto>> UploadAsync(Guid instanceId, EvidenceUploadRequest request, CancellationToken cancellationToken);

    Task<RemediationResult<IReadOnlyList<EvidenceDto>>> ListAsync(Guid instanceId, CancellationToken cancellationToken);
}

/// <summary>comment + @mention (C8 · US-023 · FR-30).</summary>
public interface ICommentService
{
    Task<RemediationResult<CommentDto>> AddAsync(Guid instanceId, AddCommentRequest request, CancellationToken cancellationToken);
}
