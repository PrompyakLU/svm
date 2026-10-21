using Microsoft.Extensions.Logging;
using SvmApi.Application.Notifications;

namespace SvmApi.Infrastructure.Integration.Mock;

/// <summary>Mock อีเมล (workshop · <c>Integration:UseMock=true</c>): ไม่ส่งจริง บันทึก log เท่านั้น — ไม่ log ที่อยู่อีเมล/เนื้อหา.</summary>
public sealed class MockEmailAdapter(ILogger<MockEmailAdapter> logger) : IEmailAdapter
{
    public Task<ChannelDeliveryResult> SendAsync(
        Guid recipientUserId, string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "integration.mock.email recipientId={RecipientId} subjectLength={SubjectLength}", recipientUserId, subject.Length);
        return Task.FromResult(new ChannelDeliveryResult(true));
    }
}

/// <summary>Mock Teams: log เท่านั้น.</summary>
public sealed class MockTeamsAdapter(ILogger<MockTeamsAdapter> logger) : ITeamsAdapter
{
    public Task<ChannelDeliveryResult> SendAsync(Guid recipientUserId, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "integration.mock.teams recipientId={RecipientId} subjectLength={SubjectLength}", recipientUserId, subject.Length);
        return Task.FromResult(new ChannelDeliveryResult(true));
    }
}

/// <summary>ใช้เมื่อ <c>UseMock=false</c> แต่ยังไม่มี adapter จริง → ส่งไม่สำเร็จ (fail-closed) และบันทึก delivery = Failed.</summary>
public sealed class UnavailableEmailAdapter : IEmailAdapter
{
    public Task<ChannelDeliveryResult> SendAsync(
        Guid recipientUserId, string toAddress, string subject, string body, CancellationToken cancellationToken)
        => Task.FromResult(new ChannelDeliveryResult(false, "EMAIL_NOT_CONFIGURED"));
}

/// <summary>ดู <see cref="UnavailableEmailAdapter"/>.</summary>
public sealed class UnavailableTeamsAdapter : ITeamsAdapter
{
    public Task<ChannelDeliveryResult> SendAsync(Guid recipientUserId, string subject, string body, CancellationToken cancellationToken)
        => Task.FromResult(new ChannelDeliveryResult(false, "TEAMS_NOT_CONFIGURED"));
}
