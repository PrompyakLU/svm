using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Verification;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class EvidenceConfiguration : IEntityTypeConfiguration<Evidence>
{
    public void Configure(EntityTypeBuilder<Evidence> builder)
    {
        builder.ToTable("trans_evidence");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.InstanceId).HasColumnName("instanceId");
        builder.Property(x => x.TaskId).HasColumnName("taskId");
        builder.Property(x => x.VerificationId).HasColumnName("verificationId");
        builder.Property(x => x.FileName).HasColumnName("fileName").HasMaxLength(300);
        builder.Property(x => x.StoragePath).HasColumnName("storagePath").HasMaxLength(500);
        builder.Property(x => x.ContentType).HasColumnName("contentType").HasMaxLength(100);
        builder.Property(x => x.FileSize).HasColumnName("fileSize");
        builder.Property(x => x.ScanStatus).HasColumnName("scanStatus").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Version).HasColumnName("version");
        builder.Property(x => x.UploadedBy).HasColumnName("uploadedBy");
        builder.Property(x => x.UploadedAt).HasColumnName("uploadedAt");

        builder.HasOne(x => x.Instance).WithMany(i => i.Evidences).HasForeignKey(x => x.InstanceId);
        builder.HasOne(x => x.Task).WithMany(t => t.Evidences).HasForeignKey(x => x.TaskId);
        builder.HasOne<VerificationRecord>().WithMany().HasForeignKey(x => x.VerificationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
