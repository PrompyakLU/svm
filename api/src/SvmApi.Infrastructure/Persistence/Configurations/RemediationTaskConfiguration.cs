using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;
using SvmApi.Domain.Remediation;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class RemediationTaskConfiguration : IEntityTypeConfiguration<RemediationTask>
{
    public void Configure(EntityTypeBuilder<RemediationTask> builder)
    {
        builder.ToTable("trans_remediationTask");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.InstanceId).HasColumnName("instanceId");
        builder.Property(x => x.OwnerUserId).HasColumnName("ownerUserId");
        builder.Property(x => x.OwnerTeam).HasColumnName("ownerTeam").HasMaxLength(200);
        builder.Property(x => x.DueDate).HasColumnName("dueDate");
        builder.Property(x => x.RiskLevel).HasColumnName("riskLevel").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Recommendation).HasColumnName("recommendation");
        builder.Property(x => x.RootCause).HasColumnName("rootCause");
        builder.Property(x => x.ActionPlan).HasColumnName("actionPlan");
        builder.Property(x => x.TargetDate).HasColumnName("targetDate");
        builder.Property(x => x.ChangeReference).HasColumnName("changeReference").HasMaxLength(200);
        builder.Property(x => x.ProgressPercent).HasColumnName("progressPercent");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Obstacle).HasColumnName("obstacle");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.CreatedBy).HasColumnName("createdBy");
        builder.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
        builder.Property(x => x.UpdatedBy).HasColumnName("updatedBy");

        builder.HasOne(x => x.Instance).WithMany(i => i.RemediationTasks).HasForeignKey(x => x.InstanceId);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.InstanceId);
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.DueDate);
    }
}
