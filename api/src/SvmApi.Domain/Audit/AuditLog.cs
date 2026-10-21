namespace SvmApi.Domain.Audit;

/// <summary>
/// trans_auditLog — บันทึกกิจกรรมควบคุม (immutable, append-only, hash-chained).
/// ห้าม update/delete แถวใดๆ (INSERT/SELECT เท่านั้น). FR-68, BR-33, NFR-20/21, EX-98/100.
/// RowHash = SHA-256 ของ record + PrevHash.
/// </summary>
public class AuditLog
{
    /// <summary>bigint IDENTITY (sequential) — ไม่ใช่ Guid.</summary>
    public long Id { get; set; }

    public Guid? UserId { get; set; }
    public string Action { get; set; } = default!;
    public string ObjectType { get; set; } = default!;
    public string ObjectId { get; set; } = default!;
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public string Result { get; set; } = default!;
    public string Source { get; set; } = default!;
    public DateTime Timestamp { get; set; }

    public string? PrevHash { get; set; }
    public string RowHash { get; set; } = default!;
}
