using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Sla;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("config_slaPolicy");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RiskLevel).HasColumnName("riskLevel").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.AssetCriticality).HasColumnName("assetCriticality").HasConversion<string?>().HasMaxLength(20);
        builder.Property(x => x.InternetExposure).HasColumnName("internetExposure");
        builder.Property(x => x.KevOnly).HasColumnName("kevOnly");
        builder.Property(x => x.AcknowledgeHours).HasColumnName("acknowledgeHours");
        builder.Property(x => x.StartHours).HasColumnName("startHours");
        builder.Property(x => x.ResolveHours).HasColumnName("resolveHours");
        builder.Property(x => x.IsActive).HasColumnName("isActive");
    }
}
