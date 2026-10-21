using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Risk;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class RiskScoreHistoryConfiguration : IEntityTypeConfiguration<RiskScoreHistory>
{
    public void Configure(EntityTypeBuilder<RiskScoreHistory> builder)
    {
        builder.ToTable("trans_riskScoreHistory");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.InstanceId).HasColumnName("instanceId");
        builder.Property(x => x.Score).HasColumnName("score").HasColumnType("decimal(5,2)");
        builder.Property(x => x.RiskLevel).HasColumnName("riskLevel").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CalculationType).HasColumnName("calculationType").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.FactorBreakdown).HasColumnName("factorBreakdown");
        builder.Property(x => x.Reason).HasColumnName("reason");
        builder.Property(x => x.PreviousScore).HasColumnName("previousScore").HasColumnType("decimal(5,2)");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.CreatedBy).HasColumnName("createdBy");

        builder.HasOne(x => x.Instance).WithMany(i => i.RiskScoreHistories).HasForeignKey(x => x.InstanceId);
        builder.HasIndex(x => new { x.InstanceId, x.CreatedAt });
    }
}
