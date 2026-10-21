using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Sla;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class SlaTimerConfiguration : IEntityTypeConfiguration<SlaTimer>
{
    public void Configure(EntityTypeBuilder<SlaTimer> builder)
    {
        builder.ToTable("trans_slaTimer");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.InstanceId).HasColumnName("instanceId");
        builder.Property(x => x.AcknowledgeDue).HasColumnName("acknowledgeDue");
        builder.Property(x => x.RemediationStartDue).HasColumnName("remediationStartDue");
        builder.Property(x => x.ResolutionDue).HasColumnName("resolutionDue");
        builder.Property(x => x.StartedAt).HasColumnName("startedAt");
        builder.Property(x => x.BreachStatus).HasColumnName("breachStatus").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PolicyId).HasColumnName("policyId");
        builder.Property(x => x.PreviousDue).HasColumnName("previousDue");
        builder.Property(x => x.EscalationLevel).HasColumnName("escalationLevel").HasDefaultValue(0);
        builder.Property(x => x.LastEscalatedAt).HasColumnName("lastEscalatedAt");

        builder.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.PolicyId);

        builder.HasIndex(x => x.InstanceId).IsUnique();
        builder.HasIndex(x => x.ResolutionDue);
        builder.HasIndex(x => x.BreachStatus);
    }
}
