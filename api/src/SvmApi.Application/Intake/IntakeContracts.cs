using SvmApi.Application.Common;

namespace SvmApi.Application.Intake;

/// <summary>
/// แถว finding ดิบ (ยังไม่ validate/normalize) — รูปแบบกลางที่ทั้ง CSV, JSON และ manual form แปลงเข้ามาก่อนส่ง DataQuality.
/// ทุก field เป็น string เพื่อให้ validation ตรวจชนิดข้อมูลเอง (EX-16) และรายงานเป็นชื่อ field โดยไม่สะท้อนค่าดิบกลับ.
/// </summary>
public sealed record RawFindingRow(
    string? AssetCode,
    string? SourceSystem,
    string? SourceRecordId,
    string? FindingName,
    string? Severity,
    string? CveId = null,
    string? CweId = null,
    string? CvssVersion = null,
    string? CvssBaseScore = null,
    string? CvssVector = null,
    string? Location = null,
    string? Port = null,
    string? Component = null,
    string? DetectedAt = null);

/// <summary>
/// คำขอลงทะเบียน finding ด้วยมือ (US-008 · FR-07). field ชุดเดียวกับ import template;
/// mandatory = assetCode, sourceSystem, findingName, severity (EX-15).
/// </summary>
public sealed record ManualFindingRequest(
    string? AssetCode,
    string? SourceSystem,
    string? SourceRecordId,
    string? FindingName,
    string? Severity,
    string? CveId,
    string? CweId,
    string? CvssVersion,
    decimal? CvssBaseScore,
    string? CvssVector,
    string? Location,
    int? Port,
    string? Component,
    DateTimeOffset? DetectedAt)
{
    /// <summary>แปลงเป็น <see cref="RawFindingRow"/> (invariant culture) เพื่อใช้ validation ชุดเดียวกับ import.</summary>
    public RawFindingRow ToRawRow() => new(
        AssetCode,
        SourceSystem,
        SourceRecordId,
        FindingName,
        Severity,
        CveId,
        CweId,
        CvssVersion,
        CvssBaseScore?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CvssVector,
        Location,
        Port?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Component,
        DetectedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>ผลของ finding หนึ่งรายการหลัง DataQuality (C4: valid?, instanceId, isDuplicate).</summary>
public static class FindingOutcomes
{
    /// <summary>สร้าง instance ใหม่.</summary>
    public const string Created = "Created";

    /// <summary>ซ้ำกับ instance เดิม (dedup key ตรง) — เชื่อมและปรับ Last Detected (FR-12).</summary>
    public const string Duplicate = "Duplicate";

    /// <summary>ใกล้เคียงหลาย/บางรายการจนตัดสินไม่ได้ — เข้าคิว dedup ให้คนตัดสิน (EX-45).</summary>
    public const string Queued = "Queued";

    /// <summary>ไม่ผ่าน validation — ไม่บันทึก/ไม่สร้าง instance.</summary>
    public const string Rejected = "Rejected";
}

/// <summary>ข้อผิดพลาดของ field หนึ่ง (code จาก <see cref="IntakeErrorCodes"/>). ไม่มีค่าดิบของผู้ใช้.</summary>
public sealed record FieldError(string Field, string Code)
{
    /// <summary>รูปแบบตาม api-spec: <c>"MISSING_MANDATORY: severity"</c>.</summary>
    public string Reason => $"{Code}: {Field}";
}

/// <summary>ผล DataQuality ของ finding หนึ่งรายการ.</summary>
public sealed record FindingProcessResult(
    string Outcome,
    Guid? FindingId,
    Guid? InstanceId,
    bool IsDuplicate,
    string? ValidationStatus,
    IReadOnlyList<string> ReviewIssues,
    IReadOnlyList<FieldError> Errors,
    bool AssetOutOfScope = false,
    bool IsRecurrence = false)
{
    public bool IsValid => Outcome != FindingOutcomes.Rejected;
}

/// <summary>ข้อผิดพลาดรายแถวในผล import (api-spec: <c>{ row, reason }</c>).</summary>
public sealed record ImportRowError(int Row, string Reason);

/// <summary>
/// ผลการนำเข้า (api-spec <c>POST /findings/import</c>) + field เพิ่มแบบ additive.
/// <c>SuccessCount = Created + Duplicate + Queued</c>. <c>Replayed</c> = true เมื่อเป็นผลเดิมของ transactionId ที่เคยนำเข้าแล้ว (BR-36/EX-17).
/// </summary>
public sealed record ImportResultDto(
    Guid BatchId,
    string Status,
    int TotalRows,
    int SuccessCount,
    int FailedCount,
    int CreatedCount,
    int DuplicateCount,
    int QueuedCount,
    int NeedsReviewCount,
    bool Replayed,
    IReadOnlyList<ImportRowError> Errors);

public sealed record ImportBatchDto(
    Guid Id,
    string FileName,
    string? TransactionId,
    int TotalRows,
    int SuccessCount,
    int FailedCount,
    string Status,
    Guid ImportedBy,
    DateTime ImportedAt);

public sealed record ImportBatchDetailDto(ImportBatchDto Batch, IReadOnlyList<ImportRowError> Errors);

public sealed record ImportBatchQuery(string? Status, int Page = 1, int PageSize = 25);

/// <summary>ผล manual registration (201).</summary>
public sealed record ManualFindingResultDto(
    Guid FindingId,
    Guid? InstanceId,
    string Outcome,
    bool IsDuplicate,
    string ValidationStatus,
    IReadOnlyList<string> ReviewIssues);

/// <summary>instance ที่อาจเป็นรายการเดียวกัน (candidate ของ dedup queue).</summary>
public sealed record DedupCandidateDto(
    Guid InstanceId,
    string Title,
    string? CveId,
    string? Location,
    int? Port,
    string? Component,
    string Status,
    DateTime LastDetectedAt);

/// <summary>รายการในคิว dedup (EX-45) — finding NeedsReview ที่ยังไม่ผูก instance.</summary>
public sealed record DedupQueueItemDto(
    Guid FindingId,
    Guid? AssetId,
    string? AssetCode,
    string FindingName,
    string SourceSystem,
    string? SourceRecordId,
    string? Severity,
    string? CveId,
    string? Location,
    int? Port,
    string? Component,
    DateTime DetectedAt,
    Guid? ImportBatchId,
    IReadOnlyList<DedupCandidateDto> Candidates);

public sealed record DedupQueueQuery(int Page = 1, int PageSize = 25);

/// <summary>การตัดสิน duplicate: <c>Duplicate</c> (ต้องระบุ instanceId) หรือ <c>Distinct</c> (สร้าง instance ใหม่).</summary>
public sealed record ResolveDuplicateRequest(string? Decision, Guid? InstanceId, string? Reason);

public sealed record ResolveDuplicateResultDto(Guid FindingId, Guid InstanceId, string Decision, string ValidationStatus);

public static class DedupDecisions
{
    public const string Duplicate = "Duplicate";
    public const string Distinct = "Distinct";
}

public static class IntakeErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";

    // file-level (EX-11..14)
    public const string FileMissing = "FILE_MISSING";
    public const string UnsupportedFormat = "UNSUPPORTED_FORMAT";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string FileEmpty = "FILE_EMPTY";
    public const string TooManyRows = "TOO_MANY_ROWS";
    public const string InvalidStructure = "INVALID_STRUCTURE";
    public const string MissingColumn = "MISSING_COLUMN";
    public const string UnknownColumn = "UNKNOWN_COLUMN";
    public const string DuplicateColumn = "DUPLICATE_COLUMN";

    // row/field-level (EX-15/16)
    public const string MissingMandatory = "MISSING_MANDATORY";
    public const string InvalidType = "INVALID_TYPE";
    public const string InvalidFormat = "INVALID_FORMAT";
    public const string TooLong = "TOO_LONG";
    public const string InvalidSeverity = "INVALID_SEVERITY";
    public const string InvalidDate = "INVALID_DATE";
    public const string InvalidPort = "INVALID_PORT";
    public const string InvalidIdentifier = "INVALID_IDENTIFIER";
    public const string InvalidScore = "INVALID_SCORE";
    public const string AssetNotFound = "ASSET_NOT_FOUND";
    public const string AssetDecommissioned = "ASSET_DECOMMISSIONED";

    // idempotency (BR-36 · EX-17)
    public const string InvalidTransactionId = "INVALID_TRANSACTION_ID";
    public const string TransactionIdConflict = "TRANSACTION_ID_CONFLICT";
    public const string ImportInProgress = "IMPORT_IN_PROGRESS";

    // dedup queue
    public const string FindingNotFound = "FINDING_NOT_FOUND";
    public const string BatchNotFound = "IMPORT_BATCH_NOT_FOUND";
    public const string NotInQueue = "FINDING_NOT_IN_DEDUP_QUEUE";
    public const string InstanceNotCandidate = "INSTANCE_NOT_CANDIDATE";
    public const string DedupKeyExists = "DEDUP_KEY_EXISTS";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}

public enum IntakeOutcome
{
    Success,
    NotFound,

    /// <summary>มีอยู่แต่อยู่นอก data scope (EX-07): ผู้เรียกบันทึก denied-access และตอบเหมือน NotFound.</summary>
    OutOfScope,

    ValidationFailed,
    Conflict,
}

public sealed record IntakeResult<T>(
    IntakeOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Code = null,
    string? Message = null)
{
    public static IntakeResult<T> Ok(T value) => new(IntakeOutcome.Success, value);

    public static IntakeResult<T> Fail(IntakeOutcome outcome, string code, string? message = null)
        => new(outcome, default, null, code, message);

    public static IntakeResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors, string code = IntakeErrorCodes.ValidationError)
        => new(IntakeOutcome.ValidationFailed, default, errors, code);
}

/// <summary>ไฟล์ที่อัปโหลด (เนื้อหาอ่านแล้ว ขนาดถูกจำกัดโดย controller ก่อน).</summary>
public sealed record ImportFile(string FileName, ReadOnlyMemory<byte> Content);

/// <summary>
/// IntakeModule (C3 · US-007/008/009 · FR-05/07/08). ทุก query กรองด้วย data scope ของผู้ใช้ปัจจุบัน;
/// การเขียนทุกครั้งอยู่ใน unit of work เดียวกับ audit row.
/// </summary>
public interface IIntakeService
{
    /// <summary>นำเข้าไฟล์ CSV/JSON (inline + batch tracking). idempotent ด้วย <paramref name="transactionId"/> (BR-36).</summary>
    Task<IntakeResult<ImportResultDto>> ImportAsync(ImportFile file, string? transactionId, CancellationToken cancellationToken);

    Task<IntakeResult<ManualFindingResultDto>> RegisterManualAsync(ManualFindingRequest request, CancellationToken cancellationToken);

    Task<IntakeResult<PagedResult<ImportBatchDto>>> ListBatchesAsync(ImportBatchQuery query, CancellationToken cancellationToken);

    Task<IntakeResult<ImportBatchDetailDto>> GetBatchAsync(Guid id, CancellationToken cancellationToken);

    Task<IntakeResult<PagedResult<DedupQueueItemDto>>> GetDedupQueueAsync(DedupQueueQuery query, CancellationToken cancellationToken);

    Task<IntakeResult<ResolveDuplicateResultDto>> ResolveDuplicateAsync(
        Guid findingId, ResolveDuplicateRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// DataQualityService (C4 · US-010/011/012 · FR-09..12): validate (mandatory/type/reference) → normalize → dedup.
/// ต้องเรียกภายใน unit of work ของผู้เรียก (เพิ่ม entity ลง DbContext และ SaveChanges แต่ไม่ commit).
/// </summary>
public interface IDataQualityService
{
    Task<FindingProcessResult> ProcessAsync(RawFindingRow row, Guid? importBatchId, CancellationToken cancellationToken);
}

/// <summary>
/// Vulnerability scanner adapter (integration.md — IScannerAdapter). MVP ใช้ file import เป็นหลัก;
/// workshop = mock เท่านั้น (ไม่เรียกระบบจริง). ผลที่ดึงมาเป็น <see cref="RawFindingRow"/> เพื่อผ่าน DataQuality ชุดเดียวกัน.
/// </summary>
public interface IScannerAdapter
{
    Task<IReadOnlyList<RawFindingRow>> PullFindingsAsync(string sourceSystem, CancellationToken cancellationToken);

    /// <summary>
    /// ขอ rescan ช่องโหว่ของ instance (FR-44 · C10). คืนผลทันที (mock จำลองผลแบบ deterministic) —
    /// ล้มเหลว/ไม่พบ target สื่อด้วย <see cref="Verification.RescanStatus"/> (EX-74/75) ไม่ใช่ exception.
    /// </summary>
    Task<Verification.RescanResponse> RequestRescanAsync(Verification.RescanRequest request, CancellationToken cancellationToken);
}
