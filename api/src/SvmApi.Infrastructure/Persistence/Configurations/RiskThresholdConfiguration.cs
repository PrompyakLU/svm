using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Risk;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class RiskThresholdConfiguration : IEntityTypeConfiguration<RiskThreshold>
{
    public void Configure(EntityTypeBuilder<RiskThreshold> builder)
    {
        builder.ToTable("config_riskThreshold");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RiskLevel).HasColumnName("riskLevel").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.MinScore).HasColumnName("minScore").HasColumnType("decimal(5,2)");
        builder.Property(x => x.MaxScore).HasColumnName("maxScore").HasColumnType("decimal(5,2)");
    }
}
