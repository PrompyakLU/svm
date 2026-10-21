using Microsoft.Extensions.Logging;
using SvmApi.Application.Sla;

namespace SvmApi.Infrastructure.Sla;

/// <summary>
/// ผู้แจ้ง escalation แบบ fallback (US-026): <b>log เป็น warning แบบมีโครงสร้างเท่านั้น</b> — ไม่ส่งการแจ้งเตือนจริง และไม่ log ข้อมูลส่วนบุคคล
/// (เฉพาะ id, ระดับ, จำนวนผู้รับ). ค่าเริ่มต้นของ <c>AddSvmSla</c> คือ <see cref="NotificationSlaEscalationNotifier"/> แล้ว;
/// ลงทะเบียนคลาสนี้ก่อนเรียก <c>AddSvmSla</c> (TryAdd) หากต้องการปิดการส่งจริงชั่วคราว.
/// </summary>
public sealed class LoggingSlaEscalationNotifier(ILogger<LoggingSlaEscalationNotifier> logger) : ISlaEscalationNotifier
{
    public Task NotifyAsync(SlaEscalation escalation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(escalation);

        logger.LogWarning(
            "sla.escalation instanceId={InstanceId} timerId={TimerId} level={Level} riskLevel={RiskLevel} urgent={Urgent} daysOverdue={DaysOverdue} recipientCount={RecipientCount}",
            escalation.InstanceId, escalation.TimerId, escalation.Level, escalation.RiskLevel,
            escalation.IsUrgent, escalation.DaysOverdue, escalation.RecipientUserIds.Count);

        return Task.CompletedTask;
    }
}
