using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Admin;

/// <summary>
/// รหัส error ของ AdministrationModule (C12 · US-031 · FR-64/65/66 · BR-40 · EX-54).
/// รหัสของ risk weight/threshold ใช้ค่าเดียวกับ <c>RiskErrorCodes</c> (<c>INVALID_RISK_WEIGHT</c>, <c>INVALID_RISK_THRESHOLD</c>).
/// </summary>
public static class AdminErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>EX-54: weight ติดลบ/ซ้ำ/ผลรวม active ≠ 1.0000 หรือชุด factor ไม่ครบ.</summary>
    public const string InvalidRiskWeight = "INVALID_RISK_WEIGHT";

    /// <summary>threshold ไม่ครอบ 0–100 ต่อเนื่อง / ซ้ำ / ระดับไม่ครบ.</summary>
    public const string InvalidRiskThreshold = "INVALID_RISK_THRESHOLD";

    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string InvalidSlaPolicy = "INVALID_SLA_POLICY";
    public const string InvalidAssignmentRule = "INVALID_ASSIGNMENT_RULE";
    public const string InvalidMasterData = "INVALID_MASTER_DATA";

    public const string StatusTransitionNotFound = "STATUS_TRANSITION_NOT_FOUND";
    public const string SlaPolicyNotFound = "SLA_POLICY_NOT_FOUND";
    public const string AssignmentRuleNotFound = "ASSIGNMENT_RULE_NOT_FOUND";
    public const string MasterDataNotFound = "MASTER_DATA_NOT_FOUND";
    public const string MasterDataTypeNotFound = "MASTER_DATA_TYPE_NOT_FOUND";

    /// <summary>master data ชนิดนี้เป็นค่าคงที่ของระบบ (enum) แก้ไขผ่าน API ไม่ได้.</summary>
    public const string MasterDataReadOnly = "MASTER_DATA_READ_ONLY";

    /// <summary>assignToUserId ชี้ไปยังผู้ใช้ที่ไม่มีอยู่.</summary>
    public const string AssigneeNotFound = "ASSIGNEE_NOT_FOUND";

    /// <summary>ซ้ำกับแถวที่มีอยู่ (unique index ชน) → 409.</summary>
    public const string DuplicateConfig = "DUPLICATE_CONFIG";

    /// <summary>version ที่ผู้ใช้ถือมาไม่ตรงกับค่าปัจจุบัน (มีคนแก้ไปก่อน) → 409.</summary>
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";

    public const string PermissionDenied = "ADMIN_PERMISSION_DENIED";
}

public enum AdminOutcome
{
    Success,
    NotFound,

    /// <summary>ไม่มีสิทธิ์ admin ที่ต้องใช้ (บังคับซ้ำในชั้น service) → 403.</summary>
    Forbidden,

    /// <summary>รูปแบบข้อมูลผิด (field ขาด/ชื่อ enum ไม่รู้จัก/ชนิดผิด) → 400.</summary>
    ValidationFailed,

    /// <summary>ผิดกติกาธุรกิจของ configuration (EX-54, BR-13 ฯลฯ) → 422.</summary>
    Unprocessable,

    /// <summary>ซ้ำ/ชนกับการแก้ไขอื่น → 409.</summary>
    Conflict,
}

public sealed record AdminResult<T>(
    AdminOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Code = null,
    string? Message = null)
{
    public bool Succeeded => Outcome == AdminOutcome.Success;

    public static AdminResult<T> Ok(T value) => new(AdminOutcome.Success, value);

    public static AdminResult<T> Fail(AdminOutcome outcome, string code, string? message = null)
        => new(outcome, default, null, code, message);

    /// <summary>แปลงผลตรวจ (issue ที่เป็น format → 400 <c>VALIDATION_ERROR</c>; มีแต่ business rule → 422 + <paramref name="businessCode"/>).</summary>
    public static AdminResult<T> Invalid(AdminValidation validation, string businessCode)
    {
        var errors = validation.ToErrors();
        return validation.HasFormatIssues
            ? new AdminResult<T>(AdminOutcome.ValidationFailed, default, errors, AdminErrorCodes.ValidationError)
            : new AdminResult<T>(AdminOutcome.Unprocessable, default, errors, businessCode);
    }
}

/// <summary>ข้อบกพร่องหนึ่งรายการ: <paramref name="Field"/> + <paramref name="Code"/> (ไม่สะท้อนค่าดิบที่ผู้ใช้ส่งมา เว้นค่าตัวเลขของ config).</summary>
public sealed record AdminIssue(string Field, string Code, bool IsBusinessRule = false);

/// <summary>ผลตรวจ configuration (pure) — ไม่มีรายการ = ผ่าน.</summary>
public sealed record AdminValidation(IReadOnlyList<AdminIssue> Issues)
{
    public static AdminValidation Valid { get; } = new([]);

    public bool IsValid => Issues.Count == 0;

    /// <summary>มี issue ที่เป็นรูปแบบข้อมูล (ไม่ใช่กติกาธุรกิจ) → ตอบ 400 แทน 422.</summary>
    public bool HasFormatIssues => Issues.Any(i => !i.IsBusinessRule);

    public IReadOnlyDictionary<string, string[]> ToErrors()
        => Issues
            .GroupBy(i => i.Field, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(i => i.Code).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

    public static AdminValidation From(IEnumerable<AdminIssue> issues)
    {
        var list = issues.ToList();
        return list.Count == 0 ? Valid : new AdminValidation(list);
    }
}

// =====================================================================================================
// master data
// =====================================================================================================

/// <summary>ชนิด master data ที่เปิดให้ผ่าน <c>/admin/master-data/{type}</c>.</summary>
public static class MasterDataTypes
{
    /// <summary>config_cvssVersion — CVSS version ที่รองรับ (code = version, isActive = isSupported). แก้ไขได้.</summary>
    public const string CvssVersions = "cvss-versions";

    /// <summary>config_businessCalendar — ปฏิทินวันทำงานสำหรับ SLA (code = yyyy-MM-dd, isActive = เป็นวันทำงาน). แก้ไขได้.</summary>
    public const string BusinessCalendar = "business-calendar";

    /// <summary>ระดับ severity/risk (RiskLevel) — ค่าคงที่ของระบบ อ่านอย่างเดียว.</summary>
    public const string Severity = "severity";

    /// <summary>ชนิด asset (AssetType) — ค่าคงที่ของระบบ อ่านอย่างเดียว.</summary>
    public const string AssetType = "asset-type";

    /// <summary>ระดับ criticality ของ asset (AssetCriticality) — ค่าคงที่ของระบบ อ่านอย่างเดียว.</summary>
    public const string AssetCriticality = "asset-criticality";

    public static readonly IReadOnlyList<MasterDataTypeDto> All =
    [
        new(CvssVersions, "CVSS versions", false),
        new(BusinessCalendar, "Business calendar", false),
        new(Severity, "Severity", true),
        new(AssetType, "Asset type", true),
        new(AssetCriticality, "Asset criticality", true),
    ];
}

public sealed record MasterDataTypeDto(string Type, string Name, bool ReadOnly);

/// <summary>
/// รายการ master data หนึ่งแถว. <c>IsActive</c>: cvss-versions = รองรับ (isSupported), business-calendar = เป็นวันทำงาน (isWorkingDay),
/// ชนิด read-only = true เสมอ. <c>Id</c> เป็น null สำหรับค่าคงที่ของระบบ.
/// </summary>
public sealed record MasterDataItemDto(Guid? Id, string Code, string? Description, bool IsActive, bool ReadOnly, string Version);

/// <summary>คำขอสร้าง/แก้ master data (PUT = แทนค่าทั้งก้อน; <c>Code</c> แก้ไม่ได้หลังสร้าง).</summary>
public sealed record MasterDataRequest(string? Code, string? Description, bool? IsActive, string? Version);

// =====================================================================================================
// status transitions
// =====================================================================================================

public sealed record StatusTransitionDto(
    Guid Id,
    string FromStatus,
    string ToStatus,
    string? RequiredPermission,
    bool RequiresEvidence,
    bool RequiresApproval,
    bool IsActive,
    string Version);

/// <summary>สร้าง transition ใหม่ (from/to แก้ไม่ได้ภายหลัง). <c>IsActive</c> ไม่ส่ง = true.</summary>
public sealed record CreateStatusTransitionRequest(
    string? FromStatus,
    string? ToStatus,
    string? RequiredPermission,
    bool? RequiresEvidence,
    bool? RequiresApproval,
    bool? IsActive);

/// <summary>แก้ transition (แทนค่าทั้งก้อน: ต้องส่ง requiresEvidence/requiresApproval/isActive ครบ; <c>RequiredPermission</c> ว่าง = ใช้ <c>instance.transition</c>).</summary>
public sealed record UpdateStatusTransitionRequest(
    string? RequiredPermission,
    bool? RequiresEvidence,
    bool? RequiresApproval,
    bool? IsActive,
    string? Version);

// =====================================================================================================
// SLA policies
// =====================================================================================================

public sealed record SlaPolicyDto(
    Guid Id,
    string RiskLevel,
    string? AssetCriticality,
    bool? InternetExposure,
    bool KevOnly,
    int AcknowledgeHours,
    int StartHours,
    int ResolveHours,
    bool IsActive,
    string Version);

/// <summary>
/// สร้าง/แก้ SLA policy. POST: <c>kevOnly</c> ไม่ส่ง = false, <c>isActive</c> ไม่ส่ง = true.
/// PUT: แทนค่าทั้งก้อน — ต้องส่ง kevOnly + isActive. ชั่วโมงเป็น "ชั่วโมงบน business calendar" (ดู <c>SlaCalculator</c>).
/// </summary>
public sealed record SlaPolicyRequest(
    string? RiskLevel,
    string? AssetCriticality,
    bool? InternetExposure,
    bool? KevOnly,
    int? AcknowledgeHours,
    int? StartHours,
    int? ResolveHours,
    bool? IsActive,
    string? Version);

/// <summary>งานคำนวณ SLA ใหม่หลังแก้ policy (ส่งเข้าคิว background — ไม่รอผล).</summary>
public sealed record SlaRecalculationRequest(IReadOnlyCollection<RiskLevel> RiskLevels, string Reason);

/// <summary>
/// ช่องทางส่งงาน "คำนวณ SLA ของ timer ที่ใช้งานอยู่ใหม่" ออกจาก request (งาน 5.3 ทิ้งไว้ให้ admin module).
/// <see cref="Enqueue"/> ต้องไม่ throw และไม่บล็อก: คืน false เมื่อคิวเต็ม (logged) — SLA monitor/คำนวณครั้งถัดไปยังแก้ไขให้ภายหลัง.
/// </summary>
public interface ISlaRecalculationScheduler
{
    /// <summary>จำนวน instance สูงสุดที่ประมวลผลต่อหนึ่งคำขอ (cap).</summary>
    public const int MaxInstancesPerRequest = 1000;

    bool Enqueue(SlaRecalculationRequest request);
}

// =====================================================================================================
// risk weights / thresholds
// =====================================================================================================

public sealed record RiskWeightDto(string Factor, decimal Weight, bool IsActive, DateTime? UpdatedAt);

/// <summary><c>ActiveSum</c> = ผลรวม weight ของแถว active (ควร = 1.0000). <c>Version</c> ส่งกลับใน PUT เพื่อกันเขียนทับ.</summary>
public sealed record RiskWeightsDto(IReadOnlyList<RiskWeightDto> Weights, decimal ActiveSum, string Version);

public sealed record RiskWeightItem(string? Factor, decimal? Weight, bool? IsActive);

/// <summary>แทนที่ชุด weight ทั้งชุดแบบ atomic (ต้องครบทุก factor; EX-54).</summary>
public sealed record ReplaceRiskWeightsRequest(IReadOnlyList<RiskWeightItem>? Weights, string? Version);

public sealed record RiskThresholdDto(string RiskLevel, decimal MinScore, decimal MaxScore);

public sealed record RiskThresholdsDto(IReadOnlyList<RiskThresholdDto> Thresholds, string Version);

public sealed record RiskThresholdItem(string? RiskLevel, decimal? MinScore, decimal? MaxScore);

/// <summary>แทนที่ threshold ทั้งชุด (ต้องครบทุก risk level และครอบ 0–100 ต่อเนื่อง).</summary>
public sealed record ReplaceRiskThresholdsRequest(IReadOnlyList<RiskThresholdItem>? Thresholds, string? Version);

/// <summary>ผล dry-run ของ <c>POST /admin/risk-weights/validate</c> (ไม่บันทึกอะไร).</summary>
public sealed record RiskWeightsValidationDto(bool IsValid, decimal ActiveSum, IReadOnlyList<string> Issues);

// =====================================================================================================
// assignment rules
// =====================================================================================================

public sealed record AssignmentRuleDto(
    Guid Id,
    string MatchType,
    string MatchValue,
    Guid? AssignToUserId,
    string? AssignToTeam,
    int Priority,
    bool IsActive,
    string Version);

/// <summary>สร้าง/แก้ assignment rule (PUT = แทนค่าทั้งก้อน; POST: isActive ไม่ส่ง = true).</summary>
public sealed record AssignmentRuleRequest(
    string? MatchType,
    string? MatchValue,
    Guid? AssignToUserId,
    string? AssignToTeam,
    int? Priority,
    bool? IsActive,
    string? Version);

// =====================================================================================================
// service
// =====================================================================================================

/// <summary>
/// AdministrationModule (C12 · US-031 · FR-64/65/66 · NFR-31 · BR-40): แก้ master data และ configuration ที่ขับ workflow/SLA/risk/assignment.
/// ทุกเมธอดบังคับสิทธิ์ <c>admin.*</c> ที่เกี่ยวข้องซ้ำในชั้น service; ทุกการเขียนอยู่ใน unit of work เดียวกับ audit row (who/when/before/after).
/// ไม่มีการลบ — ใช้ isActive; <c>Version</c> (hash ของค่าปัจจุบัน) ใช้เป็น optimistic concurrency เพราะตาราง config ไม่มี rowVersion.
/// </summary>
public interface IAdminService
{
    // master data (admin.masterdata)
    Task<AdminResult<IReadOnlyList<MasterDataTypeDto>>> ListMasterDataTypesAsync(CancellationToken cancellationToken);

    Task<AdminResult<IReadOnlyList<MasterDataItemDto>>> ListMasterDataAsync(
        string type, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    Task<AdminResult<MasterDataItemDto>> CreateMasterDataAsync(string type, MasterDataRequest? request, CancellationToken cancellationToken);

    Task<AdminResult<MasterDataItemDto>> UpdateMasterDataAsync(string type, Guid id, MasterDataRequest? request, CancellationToken cancellationToken);

    // status transitions (admin.workflow)
    Task<AdminResult<IReadOnlyList<StatusTransitionDto>>> ListStatusTransitionsAsync(CancellationToken cancellationToken);

    Task<AdminResult<StatusTransitionDto>> CreateStatusTransitionAsync(CreateStatusTransitionRequest? request, CancellationToken cancellationToken);

    Task<AdminResult<StatusTransitionDto>> UpdateStatusTransitionAsync(Guid id, UpdateStatusTransitionRequest? request, CancellationToken cancellationToken);

    // SLA policies (admin.sla)
    Task<AdminResult<IReadOnlyList<SlaPolicyDto>>> ListSlaPoliciesAsync(CancellationToken cancellationToken);

    Task<AdminResult<SlaPolicyDto>> CreateSlaPolicyAsync(SlaPolicyRequest? request, CancellationToken cancellationToken);

    Task<AdminResult<SlaPolicyDto>> UpdateSlaPolicyAsync(Guid id, SlaPolicyRequest? request, CancellationToken cancellationToken);

    // risk weights / thresholds (admin.risk)
    Task<AdminResult<RiskWeightsDto>> GetRiskWeightsAsync(CancellationToken cancellationToken);

    Task<AdminResult<RiskWeightsValidationDto>> ValidateRiskWeightsAsync(ReplaceRiskWeightsRequest? request, CancellationToken cancellationToken);

    Task<AdminResult<RiskWeightsDto>> ReplaceRiskWeightsAsync(ReplaceRiskWeightsRequest? request, CancellationToken cancellationToken);

    Task<AdminResult<RiskThresholdsDto>> GetRiskThresholdsAsync(CancellationToken cancellationToken);

    Task<AdminResult<RiskThresholdsDto>> ReplaceRiskThresholdsAsync(ReplaceRiskThresholdsRequest? request, CancellationToken cancellationToken);

    // assignment rules (admin.assignment)
    Task<AdminResult<IReadOnlyList<AssignmentRuleDto>>> ListAssignmentRulesAsync(CancellationToken cancellationToken);

    Task<AdminResult<AssignmentRuleDto>> CreateAssignmentRuleAsync(AssignmentRuleRequest? request, CancellationToken cancellationToken);

    Task<AdminResult<AssignmentRuleDto>> UpdateAssignmentRuleAsync(Guid id, AssignmentRuleRequest? request, CancellationToken cancellationToken);
}
