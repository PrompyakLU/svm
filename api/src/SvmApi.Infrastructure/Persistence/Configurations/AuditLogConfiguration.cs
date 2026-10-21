using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Audit;

namespace SvmApi.Infrastructure.Persistence.Configurations;

/// <summary>
/// trans_auditLog — append-only, hash-chained (NFR-20/21, D3-8).
/// Insert/Select เท่านั้นในระดับ app code; การห้าม UPDATE/DELETE จริงบน DB
/// ต้องบังคับด้วย DB permission (restricted login) ตอน deploy/migration script — ไม่ใช่แค่ EF Core.
/// </summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("trans_auditLog");
        builder.HasKey(x => x.Id);

        // bigint IDENTITY — รักษาลำดับ sequential ของ hash chain
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.UserId).HasColumnName("userId");
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(50);
        builder.Property(x => x.ObjectType).HasColumnName("objectType").HasMaxLength(100);
        builder.Property(x => x.ObjectId).HasColumnName("objectId").HasMaxLength(100);
        builder.Property(x => x.BeforeValue).HasColumnName("beforeValue");
        builder.Property(x => x.AfterValue).HasColumnName("afterValue");
        builder.Property(x => x.Result).HasColumnName("result").HasMaxLength(20);
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(100);
        builder.Property(x => x.Timestamp).HasColumnName("timestamp");
        builder.Property(x => x.PrevHash).HasColumnName("prevHash").HasMaxLength(64);
        builder.Property(x => x.RowHash).HasColumnName("rowHash").HasMaxLength(64).IsRequired();

        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.ObjectType, x.ObjectId });
        builder.HasIndex(x => x.Action);
    }
}
