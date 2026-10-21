using SvmApi.Application.Common;

namespace SvmApi.Application.Dashboard;

/// <summary>รหัส error ของ DashboardReportingModule (C11 · US-030).</summary>
public static class DashboardErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InstanceNotFound = "INSTANCE_NOT_FOUND";
}

public enum DashboardOutcome
{
    Success,
    NotFound,

    /// <summary>มีอยู่แต่นอก data scope (EX-07): ผู้เรียกบันทึก denied-access และตอบเหมือน NotFound (404).</summary>
    OutOfScope,

    ValidationFailed,
}

public sealed record DashboardResult<T>(
    DashboardOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Code = null)
{
    public static DashboardResult<T> Ok(T value) => new(DashboardOutcome.Success, value);

    public static DashboardResult<T> Fail(DashboardOutcome outcome, string code) => new(outcome, default, null, code);

    public static DashboardResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(DashboardOutcome.ValidationFailed, default, errors, DashboardErrorCodes.ValidationError);
}

// ====================================================================== shared small DTOs

/// <summary>คู่ key/count สำหรับกราฟ (รูปเดียวกับ <c>Count</c> ใน UI models).</summary>
public sealed record CountDto(string Key, long Count);

/// <summary>
/// สรุป SLA ของ instance ในรายการ/รายละเอียด. <c>BreachStatus</c>: OnTrack | NearDue | Breached | Met
/// (Met = SLA หยุดนับแล้วและไม่เกินกำหนด). <c>RemainingHours</c> ติดลบ = เกินกำหนด; ของ SLA ที่หยุดนับเป็นค่า ณ ตอนอ่าน.
/// </summary>
public sealed record SlaSummaryDto(
    string PolicyName,
    DateTime StartedAt,
    DateTime DueDate,
    string BreachStatus,
    double RemainingHours,
    int DaysOverdue,
    bool Running);

// ====================================================================== instance list

/// <summary>
/// query string ดิบของ <c>GET /api/v1/instances</c> (เป็นสตริงทั้งหมดเพื่อให้ validation คืน 400 รูปแบบเดียวกันพร้อม field error).
/// หลายค่า (status, riskLevel, slaStatus) คั่นด้วย comma.
/// </summary>
public sealed record InstanceListQuery(
    string? Search = null,
    string? Status = null,
    string? RiskLevel = null,
    string? Severity = null,
    string? SlaStatus = null,
    string? BusinessUnit = null,
    string? AssetCode = null,
    string? AssetId = null,
    string? AssignedTo = null,
    string? Cve = null,
    string? IsKev = null,
    string? InternetExposure = null,
    string? OpenOnly = null,
    string? Unassigned = null,
    string? AssignedToMe = null,
    string? DetectedFrom = null,
    string? DetectedTo = null,
    string? DueFrom = null,
    string? DueTo = null,
    string? AgeDaysMin = null,
    string? AgeDaysMax = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = InstanceQueryRules.DefaultPageSize);

/// <summary>
/// หนึ่งแถวของรายการ instance. รูปร่างเทียบกับ <c>VulnerabilityInstance</c> ฝั่ง UI: ชื่อ field เดิมทั้งหมด,
/// <c>riskLevel</c> เป็นชื่อ enum ของ API (Critical/High/Medium/Low/Informational), <c>assignedTo</c> = ชื่อที่แสดง.
/// </summary>
public sealed record InstanceListItemDto(
    Guid Id,
    string Code,
    string Title,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    string? BusinessUnit,
    bool InternetExposure,
    string? AssetCriticality,
    string? CveId,
    string? CweId,
    string? Location,
    int? Port,
    string? Component,
    string Status,
    decimal? RiskScore,
    string? RiskLevel,
    bool IsKev,
    Guid? AssignedToUserId,
    string? AssignedTo,
    DateTime FirstDetectedAt,
    DateTime LastDetectedAt,
    int AgeDays,
    string? SourceSystem,
    SlaSummaryDto? Sla);

// ====================================================================== instance detail

public sealed record InstanceAssetDto(
    Guid Id,
    string AssetCode,
    string Name,
    string AssetType,
    string? IpAddress,
    string? Hostname,
    string Environment,
    string? BusinessUnit,
    string? Criticality,
    bool InternetExposure,
    string AssetStatus);

/// <summary>ปัจจัยหนึ่งตัวของ risk breakdown (จาก <c>trans_riskScoreHistory.factorBreakdown</c> ล่าสุดที่เป็น Auto/Recalc).</summary>
public sealed record RiskFactorDto(string Factor, decimal Weight, decimal Normalized, decimal Contribution, string? Source);

public sealed record RiskHistoryEntryDto(
    Guid Id,
    decimal Score,
    string RiskLevel,
    string CalculationType,
    string? Reason,
    decimal? PreviousScore,
    DateTime CreatedAt,
    Guid? CreatedByUserId,
    string CreatedBy);

public sealed record StatusHistoryEntryDto(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    string? Reason,
    DateTime ChangedAt,
    Guid ChangedByUserId,
    string ChangedBy);

public sealed record EvidenceEntryDto(
    Guid Id,
    Guid? TaskId,
    string FileName,
    string Type,
    int Version,
    long FileSize,
    string ScanResult,
    DateTime UploadedAt,
    Guid UploadedByUserId,
    string UploadedBy);

public sealed record CommentEntryDto(Guid Id, string Body, DateTime CreatedAt, Guid CreatedByUserId, string Author);

public sealed record RemediationSummaryDto(
    Guid Id,
    string Title,
    Guid? AssignedToUserId,
    string? AssignedTo,
    string? Team,
    DateTime DueDate,
    int Progress,
    string? Plan,
    string? RootCause,
    DateTime? TargetDate,
    string? Obstacle,
    string Status);

public sealed record VerificationEntryDto(
    Guid Id,
    string Method,
    string Result,
    bool IndependentVerifier,
    Guid VerifiedByUserId,
    string VerifiedBy,
    DateTime VerifiedAt,
    string? Note);

/// <summary>
/// ปุ่ม workflow ที่ UI แสดงได้ (จาก <c>config_statusTransition</c> ที่ active ของสถานะปัจจุบัน).
/// <c>Permitted</c> = ผู้ใช้ปัจจุบันมี <c>instance.transition</c> และสิทธิ์ของแถวนั้น — ตัดสินจริงยังอยู่ที่ <c>POST /transition</c>.
/// </summary>
public sealed record AvailableTransitionDto(
    string ToStatus,
    bool RequiresEvidence,
    bool RequiresApproval,
    string? RequiredPermission,
    bool Permitted);

/// <summary>
/// รายละเอียด instance สำหรับหน้า detail: ใช้ <c>rowVersion</c> นี้เรียก transition/risk/verification.
/// list ย่อยทั้งหมดเรียงใหม่สุดก่อน และมีเพดานจำนวนแถว (ดู <c>InstanceQueryRules.Max*</c>).
/// </summary>
public sealed record InstanceDetailDto(
    Guid Id,
    string Code,
    string Title,
    string RowVersion,
    InstanceAssetDto Asset,
    string? CveId,
    string? CweId,
    string? Location,
    int? Port,
    string? Component,
    string Status,
    decimal? RiskScore,
    string? RiskLevel,
    bool IsKev,
    Guid? AssignedToUserId,
    string? AssignedTo,
    DateTime FirstDetectedAt,
    DateTime LastDetectedAt,
    int AgeDays,
    string? SourceSystem,
    DateTime? UpdatedAt,
    SlaSummaryDto? Sla,
    IReadOnlyList<RiskFactorDto> RiskBreakdown,
    IReadOnlyList<RiskHistoryEntryDto> RiskHistory,
    IReadOnlyList<StatusHistoryEntryDto> StatusHistory,
    IReadOnlyList<EvidenceEntryDto> Evidence,
    IReadOnlyList<CommentEntryDto> Comments,
    RemediationSummaryDto? Remediation,
    IReadOnlyList<VerificationEntryDto> Verification,
    IReadOnlyList<AvailableTransitionDto> AvailableTransitions);

// ====================================================================== dashboards

/// <summary>ตัวเลขสรุป (นับเฉพาะ instance ที่ยังเปิด เว้นแต่ระบุ) ภายใน data scope ของผู้ใช้.</summary>
public sealed record OperationsSummaryDto(
    long TotalOpen,
    long Unassigned,
    long Overdue,
    long NearDue,
    long PendingVerification,
    long Reopened,
    long CriticalOpen,
    long KevOpen,
    long MyOpenTasks,
    long ImportErrors,
    long? IntegrationErrors)
{
    public long HighOpen { get; init; }
}

public sealed record OperationsLists(
    IReadOnlyList<InstanceListItemDto> Unassigned,
    IReadOnlyList<InstanceListItemDto> Overdue,
    IReadOnlyList<InstanceListItemDto> PendingVerification,
    IReadOnlyList<InstanceListItemDto> MyTasks);

/// <summary>
/// <c>GET /dashboard/operations</c>: สิ่งที่ต้องลงมือทำ. <c>IntegrationErrors</c> เป็น null สำหรับผู้ที่ไม่ใช่ Organization:*
/// (ข้อมูล integration ไม่ผูกกับ scope จึงไม่เปิดให้ scope แคบ).
/// </summary>
public sealed record OperationsDashboardDto(
    DateTime GeneratedAt,
    bool OrganizationWide,
    OperationsSummaryDto Summary,
    OperationsLists Lists)
{
    public IReadOnlyList<CountDto> ByRiskLevel { get; init; } = [];
    public IReadOnlyList<CountDto> ByStatus { get; init; } = [];
    public IReadOnlyList<CountDto> ByBusinessUnit { get; init; } = [];
    public IReadOnlyList<CountDto> SlaByStatus { get; init; } = [];
    public double? MeanTimeToRemediateDays { get; init; }
    public IReadOnlyList<WeeklyTrendPoint> Trend { get; init; } = [];
}

public sealed record ExecutiveTotalsDto(
    long TotalInstances,
    long TotalOpen,
    long CriticalOpen,
    long HighOpen,
    long KevOpen,
    long InternetExposedOpen,
    long UnassignedOpen,
    long SlaBreached,
    long SlaNearDue,
    decimal? SlaCompliancePercent,
    double? MeanTimeToRemediateDays);

public sealed record WeeklyTrendPoint(DateTime WeekStart, long New, long NewCritical, long Resolved);

public sealed record TopRiskAssetDto(
    Guid AssetId,
    string AssetCode,
    string Name,
    string? BusinessUnit,
    long OpenCount,
    long CriticalCount,
    long HighCount,
    decimal? MaxRiskScore,
    long KevCount);

/// <summary>
/// <c>GET /dashboard/executive</c>: ภาพรวมระดับผู้บริหาร (aggregate ใน SQL ภายใน data scope).
/// <c>OverdueAging</c> = จำนวน instance ที่ยังเปิดและเกิน SLA แยกช่วงจำนวนวัน (key "0-7", "8-30", "31-90", "90+"; ผลรวม = SlaBreached).
/// </summary>
public sealed record ExecutiveDashboardDto(
    DateTime GeneratedAt,
    bool OrganizationWide,
    int Weeks,
    ExecutiveTotalsDto Totals,
    IReadOnlyList<CountDto> ByRiskLevel,
    IReadOnlyList<CountDto> ByStatus,
    IReadOnlyList<CountDto> ByBusinessUnit,
    IReadOnlyList<CountDto> SlaByStatus,
    IReadOnlyList<CountDto> OverdueAging,
    IReadOnlyList<WeeklyTrendPoint> Trend,
    IReadOnlyList<TopRiskAssetDto> TopRiskAssets);

// ====================================================================== services

/// <summary>query รายการ/รายละเอียด instance (C11 · FR-56/58). ทุกอย่างผ่าน data scope กลาง (fail-closed).</summary>
public interface IInstanceQueryService
{
    Task<DashboardResult<PagedResult<InstanceListItemDto>>> SearchAsync(InstanceListQuery query, CancellationToken cancellationToken);

    Task<DashboardResult<InstanceDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>dashboard (C11 · US-030 · FR-56/57): aggregate ใน SQL เท่านั้น ไม่โหลดทุกแถว.</summary>
public interface IDashboardService
{
    Task<DashboardResult<OperationsDashboardDto>> GetOperationsAsync(int listSize, CancellationToken cancellationToken);

    Task<DashboardResult<ExecutiveDashboardDto>> GetExecutiveAsync(int weeks, int topAssets, CancellationToken cancellationToken);
}
