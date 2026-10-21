using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Reference;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class CweConfiguration : IEntityTypeConfiguration<Cwe>
{
    public void Configure(EntityTypeBuilder<Cwe> builder)
    {
        builder.ToTable("master_cwe");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CweId).HasColumnName("cweId").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(300);
        builder.Property(x => x.Description).HasColumnName("description");

        builder.HasIndex(x => x.CweId).IsUnique();
    }
}
