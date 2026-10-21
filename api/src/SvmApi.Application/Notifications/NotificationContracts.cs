using SvmApi.Application.Common;

namespace SvmApi.Application.Notifications;

/// <summary>
/// คำขอแจ้งเตือนหนึ่งรายการ (C14 · FR-38..42). <paramref name="Type"/> เช่น <c>ASSIGNMENT</c>, <c>MENTION</c>, <c>SLA_NEAR_DUE</c>, <c>SLA_BREACH</c>
/// (≤ 50 ตัวอักษร). <paramref name="Urgent"/> = true เพิ่มช่องทางเร่งด่วน (email + Teams แบบ mock) นอกเหนือจาก in-app (FR-41).
/// </summary>
public sealed record NotificationRequest(
    Guid RecipientUserId,
    string Type,
    string Title,
    string Message,
    Guid? InstanceId = null,
    bool Urgent = false);

/// <summary>
/// NotificationModule (C14): in-app จริง (<c>trans_notification</c>) + email/Teams แบบ mock (log เท่านั้น).
/// <para>
/// <see cref="NotifyAsync(NotificationRequest, CancellationToken)"/> เขียนแถวแล้วเรียก SaveChanges บน DbContext ของ request ทันที —
/// ถ้าเรียกภายใน <see cref="IUnitOfWork"/> จะ commit พร้อม transaction ของผู้เรียก (atomic กับงานนั้น), ถ้าเรียกนอก transaction จะบันทึกเลย
/// (เหมาะกับ background job เช่น SlaMonitorJob ที่สร้าง scope เอง).
/// recipient ที่ไม่มีอยู่จริง = ข้าม + log warning (EX-73) ไม่ throw; recipient ที่ถูกปิดบัญชี = บันทึกแถวสถานะ <c>Failed</c>.
/// </para>
/// </summary>
public interface INotificationService
{
    /// <summary>แจ้งผู้รับหนึ่งราย.</summary>
    Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken);

    /// <summary>แจ้งหลายผู้รับด้วยเนื้อหาเดียวกัน (ตัด id ซ้ำ/ว่างอัตโนมัติ).</summary>
    Task NotifyManyAsync(
        IReadOnlyCollection<Guid> recipientUserIds,
        string type,
        string title,
        string message,
        Guid? instanceId,
        CancellationToken cancellationToken,
        bool urgent = false);
}

/// <summary>แถว in-app notification ที่ส่งให้ผู้รับเท่านั้น.</summary>
public sealed record NotificationDto(
    Guid Id,
    string Type,
    string Subject,
    string Body,
    Guid? RelatedInstanceId,
    bool IsRead,
    DateTime CreatedAt);

public static class NotificationErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOTIFICATION_NOT_FOUND";
}

/// <summary>ฝั่งอ่าน: <c>GET /api/v1/notifications</c> + <c>POST /api/v1/notifications/{id}/read</c> — เห็นเฉพาะของผู้ใช้ปัจจุบัน.</summary>
public interface INotificationInbox
{
    /// <summary>รายการ in-app ของผู้ใช้ปัจจุบัน (ใหม่สุดก่อน). pageSize ถูก clamp 1..100.</summary>
    Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>mark read; คืน false เมื่อไม่พบ หรือไม่ใช่ของผู้ใช้ปัจจุบัน (ตอบ 404 เหมือนกัน — ไม่เปิดเผยการมีอยู่).</summary>
    Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken);
}

/// <summary>ผลส่งผ่านช่องทางภายนอก (mock).</summary>
public sealed record ChannelDeliveryResult(bool Delivered, string? FailureCode = null);

/// <summary>Adapter อีเมล (mock = log เท่านั้น · integration.md). ไม่ส่งที่อยู่อีเมลลง log.</summary>
public interface IEmailAdapter
{
    Task<ChannelDeliveryResult> SendAsync(Guid recipientUserId, string toAddress, string subject, string body, CancellationToken cancellationToken);
}

/// <summary>Adapter Microsoft Teams (mock = log เท่านั้น · integration.md).</summary>
public interface ITeamsAdapter
{
    Task<ChannelDeliveryResult> SendAsync(Guid recipientUserId, string subject, string body, CancellationToken cancellationToken);
}
