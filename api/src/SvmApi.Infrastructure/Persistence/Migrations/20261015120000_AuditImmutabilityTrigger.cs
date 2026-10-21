using Microsoft.EntityFrameworkCore.Migrations;
using SvmApi.Infrastructure.Audit;

#nullable disable

namespace SvmApi.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// เพิ่ม INSTEAD OF UPDATE, DELETE trigger บน trans_auditLog (BR-33, NFR-20/21).
    /// ไม่มีการเปลี่ยน schema ของ model — เป็น raw SQL เท่านั้น.
    /// AuditSql.CreateImmutabilityTrigger เป็น batch เดียว (CREATE OR ALTER TRIGGER เป็นคำสั่งแรก)
    /// และ EF Core ส่ง migrationBuilder.Sql แต่ละครั้งเป็น batch แยก จึงไม่ต้องห่อด้วย EXEC.
    /// </summary>
    /// <inheritdoc />
    public partial class AuditImmutabilityTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuditSql.CreateImmutabilityTrigger);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuditSql.DropImmutabilityTrigger);
        }
    }
}
