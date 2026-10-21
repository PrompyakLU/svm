namespace SvmApi.Domain.Notifications;

public enum NotificationChannel
{
    InApp,
    Email,
    Teams,
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Failed,
    Read,
}

/// <summary>trans_notification — การแจ้งเตือนและประวัติการส่ง. FR-42, EX-71 (storm dedupe), EX-73 (invalid recipient).</summary>
public class Notification
{
    public Guid Id { get; set; }

    public Guid RecipientUserId { get; set; }

    public NotificationChannel Channel { get; set; }
    public string Type { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;

    public Guid? RelatedInstanceId { get; set; }

    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.Pending;
    public DateTime CreatedAt { get; set; }
}
