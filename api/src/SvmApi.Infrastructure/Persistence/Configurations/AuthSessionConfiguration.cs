using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("trans_authSession");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("userId");
        builder.Property(x => x.RefreshTokenHash).HasColumnName("refreshTokenHash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PreviousRefreshTokenHash).HasColumnName("previousRefreshTokenHash").HasMaxLength(64);
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.LastActivityAt).HasColumnName("lastActivityAt");
        builder.Property(x => x.AbsoluteExpiresAt).HasColumnName("absoluteExpiresAt");
        builder.Property(x => x.RevokedAt).HasColumnName("revokedAt");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.AbsoluteExpiresAt);
    }
}
