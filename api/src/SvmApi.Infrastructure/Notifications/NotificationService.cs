using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SvmApi.Application.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Notifications;
using SvmApi.Domain.Notifications;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Notifications;

/// <summary>
/// <see cref="INotificationService"/> (C14 · FR-38..42): in-app = แถวใน <c>trans_notification</c> (จริง); email/Teams = mock (log เท่านั้น)
/// และบันทึกผลส่งเป็นแถวแยกช่องทาง. ข้อความถูกล้าง control char/ตัดความยาว; ไม่ log เนื้อหา/ที่อยู่อีเมล.
/// ผู้รับที่ไม่มีอยู่ = ข้าม + log (EX-73); ผู้รับที่ปิดบัญชี = แถว in-app สถานะ Failed.
/// </summary>
public sealed class NotificationService(
    SvmDbContext db,
    IEmailAdapter email,
    ITeamsAdapter teams,
    TimeProvider clock,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await NotifyCoreAsync(
            [request.RecipientUserId], request.Type, request.Title, request.Message, request.InstanceId, request.Urgent, cancellationToken);
    }

    public async Task NotifyManyAsync(
        IReadOnlyCollection<Guid> recipientUserIds,
        string type,
        string title,
        string message,
        Guid? instanceId,
        CancellationToken cancellationToken,
        bool urgent = false)
    {
        ArgumentNullException.ThrowIfNull(recipientUserIds);
        await NotifyCoreAsync(recipientUserIds, type, title, message, instanceId, urgent, cancellationToken);
    }

    private async Task NotifyCoreAsync(
        IEnumerable<Guid> recipientIds,
        string type,
        string title,
        string message,
        Guid? instanceId,
        bool urgent,
        CancellationToken ct)
    {
        var normalizedType = NotificationRules.NormalizeType(type)
            ?? throw new ArgumentException("Notification type is required.", nameof(type));
        var subject = NotificationRules.Subject(title);
        if (subject.Length == 0)
        {
            throw new ArgumentException("Notification title is required.", nameof(title));
        }

        var body = NotificationRules.Body(message);
        var ids = NotificationRules.DistinctRecipients(recipientIds);
        if (ids.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.IsActive, u.ExpiresAt, u.Email })
            .ToDictionaryAsync(u => u.Id, ct);

        var added = 0;
        foreach (var id in ids)
        {
            if (!users.TryGetValue(id, out var recipient))
            {
                logger.LogWarning("notification.recipient.not_found recipientId={RecipientId} type={Type}", id, normalizedType);
                continue;
            }

            var active = recipient.IsActive && (recipient.ExpiresAt is null || recipient.ExpiresAt > now);
            var channels = NotificationRules.PlanChannels(urgent, active, !string.IsNullOrWhiteSpace(recipient.Email));

            foreach (var channel in channels)
            {
                var status = channel switch
                {
                    NotificationChannel.InApp => active ? DeliveryStatus.Sent : DeliveryStatus.Failed,
                    NotificationChannel.Email => await DeliverAsync(
                        id, channel, () => email.SendAsync(id, recipient.Email!, subject, body, ct), normalizedType),
                    _ => await DeliverAsync(
                        id, channel, () => teams.SendAsync(id, subject, body, ct), normalizedType),
                };

                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    RecipientUserId = id,
                    Channel = channel,
                    Type = normalizedType,
                    Subject = subject,
                    Body = body,
                    RelatedInstanceId = instanceId,
                    DeliveryStatus = status,
                    CreatedAt = now,
                });
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<DeliveryStatus> DeliverAsync(
        Guid recipientId, NotificationChannel channel, Func<Task<ChannelDeliveryResult>> send, string type)
    {
        try
        {
            var result = await send();
            if (result.Delivered)
            {
                return DeliveryStatus.Sent;
            }

            logger.LogWarning(
                "notification.delivery.failed recipientId={RecipientId} channel={Channel} type={Type} code={Code}",
                recipientId, channel, type, result.FailureCode);
            return DeliveryStatus.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "notification.delivery.failed recipientId={RecipientId} channel={Channel} type={Type} reason={Reason}",
                recipientId, channel, type, ex.GetType().Name);
            return DeliveryStatus.Failed;
        }
    }
}

/// <summary><see cref="INotificationInbox"/>: อ่าน/mark read เฉพาะแถว in-app ของผู้ใช้ปัจจุบัน (กรองด้วย recipient เสมอ).</summary>
public sealed class NotificationInbox(SvmDbContext db, ICurrentUser user) : INotificationInbox
{
    public async Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = NotificationRules.ClampPaging(page, pageSize);
        if (user.UserId is not { } userId)
        {
            return new PagedResult<NotificationDto>([], p, size, 0);
        }

        var query = db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId && n.Channel == NotificationChannel.InApp);
        if (unreadOnly)
        {
            query = query.Where(n => n.DeliveryStatus != DeliveryStatus.Read);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(n => new { n.Id, n.Type, n.Subject, n.Body, n.RelatedInstanceId, n.DeliveryStatus, n.CreatedAt })
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>(
            rows.Select(n => new NotificationDto(
                n.Id, n.Type, n.Subject, n.Body, n.RelatedInstanceId, n.DeliveryStatus == DeliveryStatus.Read, n.CreatedAt)).ToList(),
            p, size, total);
    }

    public async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return false;
        }

        var row = await db.Notifications.FirstOrDefaultAsync(
            n => n.Id == notificationId && n.RecipientUserId == userId && n.Channel == NotificationChannel.InApp, cancellationToken);
        if (row is null)
        {
            return false;
        }

        if (row.DeliveryStatus != DeliveryStatus.Read)
        {
            row.DeliveryStatus = DeliveryStatus.Read;
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
