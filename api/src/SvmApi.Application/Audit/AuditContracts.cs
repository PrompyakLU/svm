using SvmApi.Application.Common;

namespace SvmApi.Application.Audit;

/// <summary>เหตุการณ์ที่ต้อง audit. ห้ามใส่ secret/credential/token/password hash ใน Before/After.</summary>
public sealed record AuditEntry(
    Guid? UserId,
    string Action,
    string ObjectType,
    string ObjectId,
    string? BeforeValue,
    string? AfterValue,
    string Result,
    string Source);

public sealed record AuditQuery(
    string? ObjectType,
    string? ObjectId,
    Guid? UserId,
    string? Action,
    DateTime? From,
    DateTime? To,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditRecordDto(
    long Id,
    Guid? UserId,
    string Action,
    string ObjectType,
    string ObjectId,
    string? BeforeValue,
    string? AfterValue,
    string Result,
    string Source,
    DateTime Timestamp,
    string? PrevHash,
    string RowHash);

public sealed record AuditChainVerification(bool IsValid, long CheckedCount, long? FirstBrokenId);

/// <summary>
/// Audit log แบบ append-only + hash chain (FR-68, BR-33, NFR-20/21, EX-98/100).
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// ต่อ audit row เข้า hash chain ภายใน transaction ที่เปิดอยู่แล้วของ DbContext เดียวกัน ต้องเรียกภายใน
    /// <see cref="IUnitOfWork"/> เท่านั้น (ถ้าไม่มี transaction จะ throw <see cref="InvalidOperationException"/>
    /// เพื่อปฏิเสธ action ตาม EX-98). เมธอดนี้ขอ sp_getapplock (Exclusive, owner = Transaction) เพื่อ serialize
    /// การต่อ chain แล้วเรียก SaveChanges ภายใน transaction ปัจจุบันทันที (รวมการเปลี่ยนแปลงอื่นที่ค้างใน
    /// DbContext) แต่ยังไม่ commit — commit/rollback เกิดพร้อมกับ transaction ของผู้เรียก จึง atomic กับงานนั้น.
    /// </summary>
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken);

    /// <summary>สะดวกใช้: เปิด transaction เอง + append + save.</summary>
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<PagedResult<AuditRecordDto>> SearchAsync(AuditQuery query, CancellationToken cancellationToken);

    /// <summary>ตรวจ integrity ของ hash chain ทั้งตาราง (EX-100).</summary>
    Task<AuditChainVerification> VerifyChainAsync(CancellationToken cancellationToken);
}
