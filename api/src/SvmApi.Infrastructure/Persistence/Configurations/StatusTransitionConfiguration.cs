using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Workflow;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class StatusTransitionConfiguration : IEntityTypeConfiguration<StatusTransition>
{
    public void Configure(EntityTypeBuilder<StatusTransition> builder)
    {
        builder.ToTable("config_statusTransition");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.FromStatus).HasColumnName("fromStatus").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ToStatus).HasColumnName("toStatus").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.RequiredPermission).HasColumnName("requiredPermission").HasMaxLength(100);
        builder.Property(x => x.RequiresEvidence).HasColumnName("requiresEvidence");
        builder.Property(x => x.RequiresApproval).HasColumnName("requiresApproval");
        builder.Property(x => x.IsActive).HasColumnName("isActive");

        builder.HasIndex(x => new { x.FromStatus, x.ToStatus }).IsUnique();
    }
}
