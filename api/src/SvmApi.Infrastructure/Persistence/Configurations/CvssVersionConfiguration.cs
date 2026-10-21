using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Reference;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class CvssVersionConfiguration : IEntityTypeConfiguration<CvssVersion>
{
    public void Configure(EntityTypeBuilder<CvssVersion> builder)
    {
        builder.ToTable("config_cvssVersion");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Version).HasColumnName("version").HasMaxLength(10).IsRequired();
        builder.Property(x => x.IsSupported).HasColumnName("isSupported");

        builder.HasIndex(x => x.Version).IsUnique();
    }
}
