using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Notifications;
using SvmApi.Infrastructure.Configuration;
using SvmApi.Infrastructure.Integration.Mock;

namespace SvmApi.Infrastructure.Notifications;

/// <summary>
/// ลงทะเบียน NotificationModule (C14): <see cref="INotificationService"/> + <see cref="INotificationInbox"/> +
/// email/Teams adapter (mock ตาม <c>Integration:UseMock</c>; ถ้าไม่ใช้ mock จะเป็น adapter ที่ล้มเหลวแบบ fail-closed).
/// idempotent (TryAdd) — โมดูลอื่น (เช่น SlaModule) เรียกซ้ำได้.
/// </summary>
public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddSvmNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<IEmailAdapter>(sp =>
            sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.UseMock
                ? ActivatorUtilities.CreateInstance<MockEmailAdapter>(sp)
                : new UnavailableEmailAdapter());

        services.TryAddScoped<ITeamsAdapter>(sp =>
            sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.UseMock
                ? ActivatorUtilities.CreateInstance<MockTeamsAdapter>(sp)
                : new UnavailableTeamsAdapter());

        services.TryAddScoped<INotificationService, NotificationService>();
        services.TryAddScoped<INotificationInbox, NotificationInbox>();

        return services;
    }
}
