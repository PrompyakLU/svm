using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class UserScopeConfiguration : IEntityTypeConfiguration<UserScope>
{
    public void Configure(EntityTypeBuilder<UserScope> builder)
    {
        builder.ToTable("config_userScope");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("userId");
        builder.Property(x => x.ScopeType).HasColumnName("scopeType").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ScopeValue).HasColumnName("scopeValue").HasMaxLength(200);

        builder.HasOne(x => x.User).WithMany(u => u.UserScopes).HasForeignKey(x => x.UserId);
        builder.HasIndex(x => x.UserId);
    }
}
