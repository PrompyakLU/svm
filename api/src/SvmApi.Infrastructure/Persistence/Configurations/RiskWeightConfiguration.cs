using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Risk;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class RiskWeightConfiguration : IEntityTypeConfiguration<RiskWeight>
{
    public void Configure(EntityTypeBuilder<RiskWeight> builder)
    {
        builder.ToTable("config_riskWeight");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Factor).HasColumnName("factor").HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.Weight).HasColumnName("weight").HasColumnType("decimal(5,4)");
        builder.Property(x => x.IsActive).HasColumnName("isActive");
        builder.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
        builder.Property(x => x.UpdatedBy).HasColumnName("updatedBy");

        builder.HasIndex(x => x.Factor).IsUnique();
    }
}
