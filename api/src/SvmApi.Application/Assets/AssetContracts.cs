using SvmApi.Application.Common;

namespace SvmApi.Application.Assets;

/// <summary>เจ้าของ asset (master_assetOwnership). FR-03, BR-06.</summary>
public sealed record AssetOwnerDto(Guid Id, string OwnerType, Guid? OwnerUserId, string? OwnerName, string? TeamName);

/// <summary>
/// Asset read model (master_asset + owners). <see cref="Warnings"/> ใช้รหัสจาก <see cref="AssetWarnings"/>
/// (เช่น prod ไม่มี criticality = EX-28/BR-05).
/// </summary>
public sealed record AssetDto(
    Guid Id,
    string AssetCode,
    string Name,
    string AssetType,
    string? IpAddress,
    string? Hostname,
    string? Url,
    string Environment,
    string? BusinessUnit,
    string? Criticality,
    bool InternetExposure,
    string Status,
    string? CloudResourceId,
    string? RepositoryId,
    IReadOnlyList<AssetOwnerDto> Owners,
    IReadOnlyList<string> Warnings,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public static class AssetWarnings
{
    /// <summary>EX-28/BR-05: asset ใน production ที่ยังไม่มี criticality — กันการคำนวณ SLA ขั้นสุดท้ายจนกว่าจะกำหนด.</summary>
    public const string CriticalityMissing = "CRITICALITY_MISSING";

    /// <summary>BR-06: ยังไม่มีเจ้าของ (ต้องมี owner ก่อน Assigned).</summary>
    public const string NoOwner = "NO_OWNER";
}

public sealed record AssetOwnerInput(string? OwnerType, Guid? OwnerUserId, string? TeamName);

/// <summary>
/// สร้าง asset. ไม่รับ criticality — ต้องกำหนดผ่าน <c>PUT /assets/{id}/criticality</c> เพื่อให้มีเหตุผลและ audit (FR-04).
/// </summary>
public sealed record CreateAssetRequest(
    string? AssetCode,
    string? Name,
    string? AssetType,
    string? IpAddress,
    string? Hostname,
    string? Url,
    string? Environment,
    string? BusinessUnit,
    bool? InternetExposure,
    string? CloudResourceId,
    string? RepositoryId,
    IReadOnlyList<AssetOwnerInput>? Owners);

/// <summary>
/// แก้ไข asset (PUT = แทนที่ field ที่แก้ไขได้ทั้งหมด; name/assetType/environment/internetExposure จำเป็น,
/// field optional ที่เป็น null = ล้างค่า). <c>Status</c> null = คงเดิม. <c>Owners</c> null = คงเดิม, รายการ = แทนที่ทั้งชุด.
/// </summary>
public sealed record UpdateAssetRequest(
    string? Name,
    string? AssetType,
    string? IpAddress,
    string? Hostname,
    string? Url,
    string? Environment,
    string? BusinessUnit,
    bool? InternetExposure,
    string? CloudResourceId,
    string? RepositoryId,
    string? Status,
    IReadOnlyList<AssetOwnerInput>? Owners);

public sealed record CriticalityRequest(string? Criticality, string? Reason);

public sealed record AssetQuery(
    string? Search,
    string? AssetType,
    string? Environment,
    string? BusinessUnit,
    string? Criticality,
    bool? InternetExposure,
    string? Status,
    bool? CriticalityMissing,
    int Page = 1,
    int PageSize = 25);

/// <summary>
/// ผลการ sync (api-spec: <c>batchId, successCount, failedCount, unmappedCount</c>) + รายละเอียดเพิ่มเติมแบบ additive.
/// <c>SuccessCount = Created + Updated + Unchanged</c>. <c>Status</c>: Completed | IntegrationError (EX-30).
/// </summary>
public sealed record AssetSyncResult(
    Guid BatchId,
    string Status,
    int SuccessCount,
    int FailedCount,
    int UnmappedCount,
    int CreatedCount,
    int UpdatedCount,
    int UnchangedCount,
    int ReviewCount,
    IReadOnlyList<AssetSyncIssue> Issues);

/// <summary>รายการที่ sync ไม่สำเร็จ/map ไม่ได้ (ข้อความทั่วไป ไม่มีรายละเอียดภายใน).</summary>
public sealed record AssetSyncIssue(string ExternalId, string Kind, string Message);

public static class AssetSyncStatus
{
    public const string Completed = "Completed";
    public const string IntegrationError = "IntegrationError";
}

public enum AssetOutcome
{
    Success,

    /// <summary>ไม่พบ asset.</summary>
    NotFound,

    /// <summary>มี asset อยู่แต่อยู่นอก data scope ของผู้ใช้ (เข้าตรงผ่าน id — EX-07): ผู้เรียกต้องบันทึก denied-access และตอบเหมือน NotFound.</summary>
    OutOfScope,

    /// <summary>ผู้ใช้พยายามเขียน asset ให้อยู่นอก scope ของตนเอง: ผู้เรียกต้องบันทึก denied-access และตอบ 403.</summary>
    ScopeViolation,

    Conflict,
    ValidationFailed,
    BusinessRule,
}

public sealed record AssetResult<T>(
    AssetOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Code = null,
    string? Message = null)
{
    public static AssetResult<T> Ok(T value) => new(AssetOutcome.Success, value);

    public static AssetResult<T> Fail(AssetOutcome outcome, string? code = null, string? message = null)
        => new(outcome, default, null, code, message);

    public static AssetResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(AssetOutcome.ValidationFailed, default, errors, AssetErrorCodes.ValidationError);
}

public static class AssetErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "ASSET_NOT_FOUND";
    public const string OutOfScope = "OUT_OF_SCOPE";
    public const string CodeExists = "ASSET_CODE_EXISTS";
    public const string OwnerNotFound = "OWNER_NOT_FOUND";
    public const string SyncInProgress = "SYNC_IN_PROGRESS";
}

/// <summary>AssetModule (C2 · US-004/005/006 · FR-01..04). ทุก query/by-id กรองด้วย data scope ของผู้ใช้ปัจจุบัน.</summary>
public interface IAssetService
{
    Task<AssetResult<PagedResult<AssetDto>>> SearchAsync(AssetQuery query, CancellationToken cancellationToken);

    Task<AssetResult<AssetDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<AssetResult<AssetDto>> CreateAsync(CreateAssetRequest request, CancellationToken cancellationToken);

    Task<AssetResult<AssetDto>> UpdateAsync(Guid id, UpdateAssetRequest request, CancellationToken cancellationToken);

    Task<AssetResult<AssetDto>> SetCriticalityAsync(Guid id, CriticalityRequest request, CancellationToken cancellationToken);

    /// <summary>ประวัติ criticality (ผู้แก้ไข/เวลา/เหตุผล — FR-04) จาก audit trail; scoped เหมือน <see cref="GetAsync"/>.</summary>
    Task<AssetResult<IReadOnlyList<CriticalityHistoryEntryDto>>> GetCriticalityHistoryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>sync จาก <see cref="ICmdbAdapter"/> (mock ใน workshop) — idempotent ด้วย assetCode, ไม่ลบ asset ที่หาย (BR-38).</summary>
    Task<AssetResult<AssetSyncResult>> SyncAsync(CancellationToken cancellationToken);
}

/// <summary>
/// ข้อมูล asset จาก CMDB (ค่าดิบ ยังไม่ validate/map). <see cref="ExternalId"/> = assetCode (key ของ upsert).
/// </summary>
public sealed record CmdbAssetRecord(
    string? ExternalId,
    string? Name,
    string? AssetType,
    string? Environment,
    string? BusinessUnit,
    string? Criticality,
    bool InternetExposure,
    string? IpAddress = null,
    string? Hostname = null,
    string? Url = null,
    string? CloudResourceId = null,
    string? RepositoryId = null,
    string? Status = null,
    string? OwnerUsername = null,
    string? OwnerType = null,
    string? TeamName = null);

/// <summary>CMDB ไม่พร้อมใช้งาน/ส่งข้อมูลไม่ได้ (EX-30). Message ต้องเป็นข้อความทั่วไป ไม่มี secret/รายละเอียดภายใน.</summary>
public sealed class CmdbUnavailableException : Exception
{
    public CmdbUnavailableException()
    {
    }

    public CmdbUnavailableException(string message)
        : base(message)
    {
    }

    public CmdbUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// CMDB / IT Asset Management adapter (integration.md). workshop = mock เท่านั้น (ห้ามเรียกระบบจริง).
/// Contract: ส่งรายการ asset ล่าสุดทั้งหมด; ถ้าไม่พร้อมให้ throw <see cref="CmdbUnavailableException"/>.
/// </summary>
public interface ICmdbAdapter
{
    Task<IReadOnlyList<CmdbAssetRecord>> GetAssetsAsync(CancellationToken cancellationToken);

    Task<CmdbAssetRecord?> GetAssetByIdAsync(string assetCode, CancellationToken cancellationToken);
}
