using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;
using SvmApi.Domain.Remediation;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class AssignmentRuleConfiguration : IEntityTypeConfiguration<AssignmentRule>
{
    public void Configure(EntityTypeBuilder<AssignmentRule> builder)
    {
        builder.ToTable("config_assignmentRule");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.MatchType).HasColumnName("matchType").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.MatchValue).HasColumnName("matchValue").HasMaxLength(200);
        builder.Property(x => x.AssignToUserId).HasColumnName("assignToUserId");
        builder.Property(x => x.AssignToTeam).HasColumnName("assignToTeam").HasMaxLength(200);
        builder.Property(x => x.Priority).HasColumnName("priority");
        builder.Property(x => x.IsActive).HasColumnName("isActive");

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AssignToUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
