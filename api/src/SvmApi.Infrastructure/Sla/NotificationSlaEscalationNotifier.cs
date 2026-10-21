using Microsoft.Extensions.Logging;
using SvmApi.Application.Notifications;
using SvmApi.Application.Sla;

namespace SvmApi.Infrastructure.Sla;

/// <summary>
/// <see cref="ISlaEscalationNotifier"/> จริง (US-026 · FR-38..42): ส่ง escalation ผ่าน <see cref="INotificationService"/>
/// (in-app จริง; ถ้า <see cref="SlaEscalation.IsUrgent"/> เพิ่ม email/Teams แบบ mock ตาม FR-41).
/// ข้อความสร้างโดย <see cref="SlaEscalationMessages"/> (pure · ไม่มี PII/secret). ไม่ throw เมื่อส่งไม่สำเร็จ
/// (ตามสัญญาของ <see cref="ISlaEscalationNotifier"/>): log เฉพาะ id/ระดับ/จำนวนผู้รับ แล้วปล่อยให้ job ทำรายการถัดไป.
/// <see cref="LoggingSlaEscalationNotifier"/> ยังคงอยู่เป็น fallback (ลงทะเบียนแทนได้ถ้าต้องปิดการส่งจริง).
/// </summary>
public sealed class NotificationSlaEscalationNotifier(
    INotificationService notifications,
    ILogger<NotificationSlaEscalationNotifier> logger) : ISlaEscalationNotifier
{
    public async Task NotifyAsync(SlaEscalation escalation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(escalation);

        var message = SlaEscalationMessages.Build(escalation);
        if (message is null || escalation.RecipientUserIds.Count == 0)
        {
            logger.LogDebug(
                "sla.escalation.notify.skipped instanceId={InstanceId} level={Level} recipientCount={RecipientCount}",
                escalation.InstanceId, escalation.Level, escalation.RecipientUserIds.Count);
            return;
        }

        try
        {
            await notifications.NotifyManyAsync(
                escalation.RecipientUserIds.ToList(),
                message.Type,
                message.Title,
                message.Message,
                escalation.InstanceId,
                cancellationToken,
                message.Urgent);

            logger.LogInformation(
                "sla.escalation.notified instanceId={InstanceId} timerId={TimerId} level={Level} urgent={Urgent} recipientCount={RecipientCount}",
                escalation.InstanceId, escalation.TimerId, escalation.Level, message.Urgent, escalation.RecipientUserIds.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                "sla.escalation.notify.failed instanceId={InstanceId} timerId={TimerId} level={Level} reason={Reason}",
                escalation.InstanceId, escalation.TimerId, escalation.Level, ex.GetType().Name);
        }
    }
}
