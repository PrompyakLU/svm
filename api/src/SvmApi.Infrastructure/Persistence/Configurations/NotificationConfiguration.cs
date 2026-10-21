using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SvmApi.Domain.Access;
using SvmApi.Domain.Notifications;

namespace SvmApi.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("trans_notification");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RecipientUserId).HasColumnName("recipientUserId");
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(50);
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(300);
        builder.Property(x => x.Body).HasColumnName("body");
        builder.Property(x => x.RelatedInstanceId).HasColumnName("relatedInstanceId");
        builder.Property(x => x.DeliveryStatus).HasColumnName("deliveryStatus").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.RecipientUserId, x.DeliveryStatus });
        builder.HasIndex(x => x.CreatedAt);
    }
}
