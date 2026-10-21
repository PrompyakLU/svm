using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Assets;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("master_asset");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.AssetCode).HasColumnName("assetCode").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(300);
        builder.Property(x => x.AssetType).HasColumnName("assetType").HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.IpAddress).HasColumnName("ipAddress").HasMaxLength(45);
        builder.Property(x => x.Hostname).HasColumnName("hostname").HasMaxLength(300);
        builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(500);
        builder.Property(x => x.Environment).HasColumnName("environment").HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.BusinessUnit).HasColumnName("businessUnit").HasMaxLength(200);
        builder.Property(x => x.Criticality).HasColumnName("criticality").HasConversion<string?>().HasMaxLength(20);
        builder.Property(x => x.InternetExposure).HasColumnName("internetExposure");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.CloudResourceId).HasColumnName("cloudResourceId").HasMaxLength(300);
        builder.Property(x => x.RepositoryId).HasColumnName("repositoryId").HasMaxLength(300);
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.CreatedBy).HasColumnName("createdBy");
        builder.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
        builder.Property(x => x.UpdatedBy).HasColumnName("updatedBy");

        builder.HasIndex(x => x.AssetCode).IsUnique();
        builder.HasIndex(x => x.BusinessUnit);
        builder.HasIndex(x => x.Criticality);
        builder.HasIndex(x => x.InternetExposure);
        builder.HasIndex(x => x.Status);
    }
}
