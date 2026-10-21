using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Reference;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class CveConfiguration : IEntityTypeConfiguration<Cve>
{
    public void Configure(EntityTypeBuilder<Cve> builder)
    {
        builder.ToTable("master_cve");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CveId).HasColumnName("cveId").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.PublishedDate).HasColumnName("publishedDate");
        builder.Property(x => x.IsKev).HasColumnName("isKev");
        builder.Property(x => x.KevDateAdded).HasColumnName("kevDateAdded");

        builder.HasIndex(x => x.CveId).IsUnique();
    }
}
