using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("config_importBatch");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.FileName).HasColumnName("fileName").HasMaxLength(300);
        builder.Property(x => x.TransactionId).HasColumnName("transactionId").HasMaxLength(200);
        builder.Property(x => x.TotalRows).HasColumnName("totalRows");
        builder.Property(x => x.SuccessCount).HasColumnName("successCount");
        builder.Property(x => x.FailedCount).HasColumnName("failedCount");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ImportedBy).HasColumnName("importedBy");
        builder.Property(x => x.ImportedAt).HasColumnName("importedAt");
        builder.Property(x => x.ErrorDetail).HasColumnName("errorDetail");

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ImportedBy).OnDelete(DeleteBehavior.Restrict);

        // BR-36: idempotency — unique เฉพาะ record ที่มี transactionId (filtered, อนุญาต null ซ้ำได้)
        builder.HasIndex(x => x.TransactionId).IsUnique().HasFilter("[transactionId] IS NOT NULL");
    }
}
