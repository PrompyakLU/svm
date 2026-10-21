using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;
using SvmApi.Domain.Assets;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class AssetOwnershipConfiguration : IEntityTypeConfiguration<AssetOwnership>
{
    public void Configure(EntityTypeBuilder<AssetOwnership> builder)
    {
        builder.ToTable("master_assetOwnership");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.AssetId).HasColumnName("assetId");
        builder.Property(x => x.OwnerUserId).HasColumnName("ownerUserId");
        builder.Property(x => x.OwnerType).HasColumnName("ownerType").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.TeamName).HasColumnName("teamName").HasMaxLength(200);

        builder.HasOne(x => x.Asset).WithMany(a => a.Ownerships).HasForeignKey(x => x.AssetId);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.AssetId, x.OwnerType });
    }
}
