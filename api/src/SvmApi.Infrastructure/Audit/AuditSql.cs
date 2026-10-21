namespace SvmApi.Infrastructure.Audit;

/// <summary>
/// SQL สำหรับ migration: ห้าม UPDATE/DELETE บน trans_auditLog ในระดับ DB (BR-33, NFR-20/21).
/// ใช้กับ <c>migrationBuilder.Sql(AuditSql.CreateImmutabilityTrigger)</c> ใน Up และ
/// <c>migrationBuilder.Sql(AuditSql.DropImmutabilityTrigger)</c> ใน Down.
/// หมายเหตุ: TRUNCATE ไม่เรียก trigger — ต้องจำกัดด้วย DB permission ตอน deploy.
/// </summary>
public static class AuditSql
{
    /// <summary>ชื่อ trigger.</summary>
    public const string ImmutabilityTriggerName = "trg_trans_auditLog_immutable";

    /// <summary>
    /// INSTEAD OF UPDATE, DELETE → THROW เสมอ (idempotent ด้วย CREATE OR ALTER).
    /// ต้องรันเป็น batch เดียว (CREATE TRIGGER ต้องเป็นคำสั่งแรกของ batch).
    /// </summary>
    public const string CreateImmutabilityTrigger = """
        CREATE OR ALTER TRIGGER [dbo].[trg_trans_auditLog_immutable]
        ON [dbo].[trans_auditLog]
        INSTEAD OF UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51000, N'trans_auditLog is append-only: UPDATE and DELETE are not permitted.', 1;
        END
        """;

    /// <summary>ลบ trigger (ใช้ใน Down).</summary>
    public const string DropImmutabilityTrigger =
        "DROP TRIGGER IF EXISTS [dbo].[trg_trans_auditLog_immutable];";
}
