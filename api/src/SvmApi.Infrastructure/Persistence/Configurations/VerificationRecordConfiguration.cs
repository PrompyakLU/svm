using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;
using SvmApi.Domain.Verification;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class VerificationRecordConfiguration : IEntityTypeConfiguration<VerificationRecord>
{
    public void Configure(EntityTypeBuilder<VerificationRecord> builder)
    {
        builder.ToTable("trans_verificationRecord");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.InstanceId).HasColumnName("instanceId");
        builder.Property(x => x.Result).HasColumnName("result").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.IsIndependent).HasColumnName("isIndependent");
        builder.Property(x => x.VerifiedBy).HasColumnName("verifiedBy");
        builder.Property(x => x.VerifiedAt).HasColumnName("verifiedAt");
        builder.Property(x => x.Notes).HasColumnName("notes");

        builder.HasOne(x => x.Instance).WithMany(i => i.VerificationRecords).HasForeignKey(x => x.InstanceId);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.VerifiedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
