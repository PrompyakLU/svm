using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;
using SvmApi.Domain.Workflow;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class StatusHistoryConfiguration : IEntityTypeConfiguration<StatusHistory>
{
    public void Configure(EntityTypeBuilder<StatusHistory> builder)
    {
        builder.ToTable("trans_statusHistory");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.InstanceId).HasColumnName("instanceId");
        builder.Property(x => x.FromStatus).HasColumnName("fromStatus").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ToStatus).HasColumnName("toStatus").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Reason).HasColumnName("reason");
        builder.Property(x => x.ChangedBy).HasColumnName("changedBy");
        builder.Property(x => x.ChangedAt).HasColumnName("changedAt");

        builder.HasOne(x => x.Instance).WithMany(i => i.StatusHistories).HasForeignKey(x => x.InstanceId);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.InstanceId, x.ChangedAt });
    }
}
