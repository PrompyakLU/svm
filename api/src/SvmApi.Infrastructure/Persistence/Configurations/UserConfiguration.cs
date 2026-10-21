using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("config_user");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("displayName").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(256);
        builder.Property(x => x.PasswordHash).HasColumnName("passwordHash");
        builder.Property(x => x.IsActive).HasColumnName("isActive");
        builder.Property(x => x.ExpiresAt).HasColumnName("expiresAt");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.CreatedBy).HasColumnName("createdBy");
        builder.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
        builder.Property(x => x.UpdatedBy).HasColumnName("updatedBy");

        builder.HasIndex(x => x.Username).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}
