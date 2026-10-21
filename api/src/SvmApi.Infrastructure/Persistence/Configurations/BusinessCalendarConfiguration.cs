using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Sla;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class BusinessCalendarConfiguration : IEntityTypeConfiguration<BusinessCalendar>
{
    public void Configure(EntityTypeBuilder<BusinessCalendar> builder)
    {
        builder.ToTable("config_businessCalendar");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Date).HasColumnName("date");
        builder.Property(x => x.IsWorkingDay).HasColumnName("isWorkingDay");
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(200);

        builder.HasIndex(x => x.Date).IsUnique();
    }
}
