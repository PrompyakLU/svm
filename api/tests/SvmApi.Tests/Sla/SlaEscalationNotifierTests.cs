using Microsoft.Extensions.Logging.Abstractions;
using SvmApi.Application.Notifications;
using SvmApi.Application.Sla;
using SvmApi.Infrastructure.Sla;

namespace SvmApi.Tests.Sla;

/// <summary>
/// ข้อความ + ผู้รับของ SLA escalation (US-026 · FR-39..41 · task 5.3 follow-up):
/// ประเภท/หัวข้อ/ข้อความตามระดับ, ไม่มี PII, urgent ส่งต่อ, ผู้รับไม่ว่าง, ไม่ throw เมื่อส่งไม่สำเร็จ.
/// </summary>
public class SlaEscalationNotifierTests
{
    private static readonly DateTime Due = new(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc);

    private static SlaEscalation Escalation(
        SlaEscalationLevel level, bool urgent = false, int daysOverdue = 0, IReadOnlyList<Guid>? recipients = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), level, "Critical", urgent, daysOverdue, Due, recipients ?? [Guid.NewGuid()]);

    private sealed class FakeNotifications : INotificationService
    {
        public List<(IReadOnlyCollection<Guid> Recipients, string Type, string Title, string Message, Guid? InstanceId, bool Urgent)> Calls { get; } = [];

        public bool Throw { get; init; }

        public Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken)
            => NotifyManyAsync([request.RecipientUserId], request.Type, request.Title, request.Message, request.InstanceId, cancellationToken, request.Urgent);

        public Task NotifyManyAsync(
            IReadOnlyCollection<Guid> recipientUserIds, string type, string title, string message, Guid? instanceId,
            CancellationToken cancellationToken, bool urgent = false)
        {
            if (Throw)
            {
                throw new InvalidOperationException("boom");
            }

            Calls.Add((recipientUserIds, type, title, message, instanceId, urgent));
            return Task.CompletedTask;
        }
    }

    // ------------------------------------------------------------ message building (pure)

    [Theory]
    [InlineData(SlaEscalationLevel.NearDue, SlaEscalationMessages.TypeNearDue)]
    [InlineData(SlaEscalationLevel.Breached, SlaEscalationMessages.TypeBreach)]
    [InlineData(SlaEscalationLevel.Overdue, SlaEscalationMessages.TypeOverdue)]
    public void Level_maps_to_notification_type(SlaEscalationLevel level, string expectedType)
    {
        var m = SlaEscalationMessages.Build(Escalation(level, daysOverdue: 4));

        Assert.NotNull(m);
        Assert.Equal(expectedType, m!.Type);
        Assert.False(string.IsNullOrWhiteSpace(m.Title));
        Assert.Contains("Critical", m.Message);
        Assert.Contains("2026-03-15", m.Message);
        Assert.InRange(m.Type.Length, 1, NotificationRules.MaxTypeLength);
        Assert.InRange(m.Title.Length, 1, NotificationRules.MaxSubjectLength);
    }

    [Fact]
    public void Overdue_message_mentions_days_overdue()
    {
        var m = SlaEscalationMessages.Build(Escalation(SlaEscalationLevel.Overdue, daysOverdue: 5));

        Assert.Contains("5 วัน", m!.Message);
    }

    [Fact]
    public void None_level_has_no_message()
        => Assert.Null(SlaEscalationMessages.Build(Escalation(SlaEscalationLevel.None)));

    [Fact]
    public void Negative_days_overdue_is_clamped()
    {
        var m = SlaEscalationMessages.Build(Escalation(SlaEscalationLevel.Overdue, daysOverdue: -3));

        // เทียบ ordinal: การเทียบ culture-sensitive บน Windows ข้ามเครื่องหมาย "-" จึงไปเจอ "3" ใน "2026-03-15"
        Assert.DoesNotContain("-3", m!.Message, StringComparison.Ordinal);
        Assert.Contains("0 วัน", m.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Urgent_flag_is_carried_into_message(bool urgent)
        => Assert.Equal(urgent, SlaEscalationMessages.Build(Escalation(SlaEscalationLevel.Breached, urgent))!.Urgent);

    [Fact]
    public void Message_contains_no_ids_or_emails()
    {
        var recipient = Guid.NewGuid();
        var e = Escalation(SlaEscalationLevel.Breached, recipients: [recipient]);

        var m = SlaEscalationMessages.Build(e)!;
        var text = m.Title + " " + m.Message;

        Assert.DoesNotContain(recipient.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(e.InstanceId.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ notifier (recipients / urgent)

    [Fact]
    public async Task Notifier_sends_one_call_to_all_recipients_with_instance_and_urgent_flag()
    {
        var fake = new FakeNotifications();
        var notifier = new NotificationSlaEscalationNotifier(fake, NullLogger<NotificationSlaEscalationNotifier>.Instance);
        var recipients = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var e = Escalation(SlaEscalationLevel.Breached, urgent: true, recipients: recipients);

        await notifier.NotifyAsync(e, CancellationToken.None);

        var call = Assert.Single(fake.Calls);
        Assert.Equal(recipients, call.Recipients);
        Assert.Equal(SlaEscalationMessages.TypeBreach, call.Type);
        Assert.Equal(e.InstanceId, call.InstanceId);
        Assert.True(call.Urgent);
    }

    [Fact]
    public async Task Notifier_skips_when_there_are_no_recipients()
    {
        var fake = new FakeNotifications();
        var notifier = new NotificationSlaEscalationNotifier(fake, NullLogger<NotificationSlaEscalationNotifier>.Instance);

        await notifier.NotifyAsync(Escalation(SlaEscalationLevel.NearDue, recipients: []), CancellationToken.None);

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Notifier_skips_none_level()
    {
        var fake = new FakeNotifications();
        var notifier = new NotificationSlaEscalationNotifier(fake, NullLogger<NotificationSlaEscalationNotifier>.Instance);

        await notifier.NotifyAsync(Escalation(SlaEscalationLevel.None), CancellationToken.None);

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Notifier_does_not_throw_when_notification_service_fails()
    {
        var notifier = new NotificationSlaEscalationNotifier(
            new FakeNotifications { Throw = true }, NullLogger<NotificationSlaEscalationNotifier>.Instance);

        await notifier.NotifyAsync(Escalation(SlaEscalationLevel.Overdue, daysOverdue: 3), CancellationToken.None);
    }

    [Fact]
    public async Task Notifier_propagates_cancellation()
    {
        var notifier = new NotificationSlaEscalationNotifier(
            new CancellingNotifications(), NullLogger<NotificationSlaEscalationNotifier>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => notifier.NotifyAsync(Escalation(SlaEscalationLevel.Breached), CancellationToken.None));
    }

    private sealed class CancellingNotifications : INotificationService
    {
        public Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken)
            => throw new OperationCanceledException();

        public Task NotifyManyAsync(
            IReadOnlyCollection<Guid> recipientUserIds, string type, string title, string message, Guid? instanceId,
            CancellationToken cancellationToken, bool urgent = false)
            => throw new OperationCanceledException();
    }
}
