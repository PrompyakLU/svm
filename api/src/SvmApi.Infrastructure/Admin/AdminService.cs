using System.Text.Encodings.Web;
using System.Text.Json;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Admin;
using SvmApi.Application.Audit;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Infrastructure.Persistence;
using SvmApi.Infrastructure.Reference;

namespace SvmApi.Infrastructure.Admin;

/// <summary>
/// <see cref="IAdminService"/> บน <see cref="SvmDbContext"/> (C12 · US-031 · FR-64/65/66 · NFR-31 · BR-40 · EX-54).
/// <list type="bullet">
/// <item>ทุกเมธอดตรวจสิทธิ์ <c>admin.*</c> ซ้ำในชั้น service (นอกเหนือจาก <c>[Authorize]</c> ที่ controller) — ไม่มีสิทธิ์ = <see cref="AdminOutcome.Forbidden"/>.</item>
/// <item>การเขียนทุกครั้งอยู่ใน <see cref="IUnitOfWork"/> เดียวกับ audit row (who = ผู้ใช้ปัจจุบัน, when = timestamp ของ audit, before/after = ค่า config);
///   audit append ไม่ได้ = ไม่บันทึก config (EX-98). ค่าใน audit เป็น config เท่านั้น ไม่มีข้อมูลส่วนบุคคล/secret.</item>
/// <item>กติกา validation อยู่ใน <c>SvmApi.Application.Admin.*AdminRules</c> (pure, unit-test ได้); service มีหน้าที่โหลด/บันทึกและตรวจกับข้อมูลจริง (id ที่ไม่มีอยู่ = 404).</item>
/// <item>ไม่มีการลบ (ใช้ isActive). ตาราง config ไม่มี rowVersion → <c>Version</c> = hash ของค่าปัจจุบัน ใช้เป็น optimistic concurrency (ไม่ตรง = 409).</item>
/// <item>ไม่มี cache ของ config ที่นี่ — risk/SLA/workflow/assignment อ่านจาก DB ทุกครั้ง จึงมีผลทันที; เฉพาะ <see cref="ReferenceSnapshotCache"/> (CVSS version) ที่ถูก invalidate หลังแก้.</item>
/// </list>
/// </summary>
public sealed partial class AdminService(
    SvmDbContext db,
    IUnitOfWork unitOfWork,
    IAuditService audit,
    ICurrentUser user,
    TimeProvider clock,
    ReferenceSnapshotCache referenceCache,
    ISlaRecalculationScheduler slaRecalculation,
    ILogger<AdminService> logger) : IAdminService
{
    public const string AuditSource = "AdminService";

    public const string ActionMasterDataCreate = "admin.masterdata.create";
    public const string ActionMasterDataUpdate = "admin.masterdata.update";
    public const string ActionTransitionCreate = "admin.statustransition.create";
    public const string ActionTransitionUpdate = "admin.statustransition.update";
    public const string ActionSlaPolicyCreate = "admin.slapolicy.create";
    public const string ActionSlaPolicyUpdate = "admin.slapolicy.update";
    public const string ActionRiskWeightsReplace = "admin.riskweights.replace";
    public const string ActionRiskThresholdsReplace = "admin.riskthresholds.replace";
    public const string ActionAssignmentRuleCreate = "admin.assignmentrule.create";
    public const string ActionAssignmentRuleUpdate = "admin.assignmentrule.update";

    private static readonly JsonSerializerOptions AuditJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    /// <summary>คืน <see cref="AdminOutcome.Forbidden"/> เมื่อผู้เรียกไม่มีสิทธิ์; null = ผ่าน.</summary>
    private AdminResult<T>? Deny<T>(string permission)
        => user.IsAuthenticated && user.HasPermission(permission)
            ? null
            : AdminResult<T>.Fail(AdminOutcome.Forbidden, AdminErrorCodes.PermissionDenied, "You do not have permission to manage this configuration.");

    /// <summary>
    /// รันงานเขียนใน unit of work: ล้าง change tracker ต้นรอบ (execution strategy อาจ retry ทั้งก้อน), แปลง unique/concurrency ชนเป็น 409.
    /// </summary>
    private async Task<AdminResult<T>> WriteAsync<T>(Func<CancellationToken, Task<AdminResult<T>>> work, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteAsync(async ct =>
            {
                db.ChangeTracker.Clear();
                // Serialize the read/version-check/write sequence across application instances.
                // Config entities have no rowversion; the transaction owns and releases this lock.
                var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
                await db.Database.ExecuteSqlRawAsync(
                    "EXEC @result = sys.sp_getapplock @Resource = N'svm.admin.config', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000",
                    [result], ct);
                if ((int)result.Value < 0)
                {
                    return VersionConflict<T>();
                }
                var outcome = await work(ct);
                if (!outcome.Succeeded) db.ChangeTracker.Clear();
                return outcome;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return VersionConflict<T>();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            logger.LogWarning("admin.config.save_conflict reason={Reason}", ex.GetType().Name);
            return AdminResult<T>.Fail(
                AdminOutcome.Conflict, AdminErrorCodes.DuplicateConfig,
                "The configuration conflicts with an existing row or was changed concurrently. Reload and retry.");
        }
    }

    private Task AppendAuditAsync(
        string action, string objectType, string objectId, object? before, object? after, CancellationToken cancellationToken)
        => audit.AppendAsync(
            new AuditEntry(
                user.UserId, action, objectType, objectId,
                before is null ? null : JsonSerializer.Serialize(before, AuditJson),
                after is null ? null : JsonSerializer.Serialize(after, AuditJson),
                "Success", AuditSource),
            cancellationToken);

    private static AdminResult<T> VersionConflict<T>()
        => AdminResult<T>.Fail(
            AdminOutcome.Conflict, AdminErrorCodes.ConcurrencyConflict,
            "The configuration was changed by someone else. Reload and retry.");

    private void EnqueueSlaRecalculation(IReadOnlyCollection<SvmApi.Domain.Vulnerabilities.RiskLevel> levels, string reason)
    {
        if (levels.Count == 0)
        {
            return;
        }

        // ไม่ throw ไม่บล็อก: คิวเต็ม = log แล้วไปต่อ (config ถูกบันทึกแล้ว; timer จะถูกคำนวณเมื่อมี trigger ถัดไป)
        var queued = slaRecalculation.Enqueue(new SlaRecalculationRequest(levels, reason));
        if (!queued)
        {
            logger.LogWarning("admin.sla.recalculation.not_queued reason={Reason} levels={Levels}", reason, string.Join(',', levels));
        }
    }
}
