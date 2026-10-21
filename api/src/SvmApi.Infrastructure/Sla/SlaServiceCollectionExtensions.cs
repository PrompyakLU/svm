using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SvmApi.Application.Sla;
using SvmApi.Infrastructure.Notifications;

namespace SvmApi.Infrastructure.Sla;

/// <summary>
/// ลงทะเบียน SlaModule (C9): <see cref="ISlaService"/>, <see cref="SlaMonitor"/>, <see cref="SlaMonitorJob"/> และ
/// <see cref="ISlaEscalationNotifier"/> ค่าเริ่มต้น = <see cref="NotificationSlaEscalationNotifier"/> (แจ้งผ่าน NotificationModule · เรียก
/// <c>AddSvmNotifications</c> ให้เสมอ). <see cref="LoggingSlaEscalationNotifier"/> เป็น fallback (log เท่านั้น).
/// idempotent (TryAdd) — ลงทะเบียน notifier ของตนก่อนเพื่อแทนที่ค่าเริ่มต้น.
/// ตัวเลือกอยู่ใน section <c>Sla</c> (<see cref="SlaOptions"/>).
/// </summary>
public static class SlaServiceCollectionExtensions
{
    public static IServiceCollection AddSvmSla(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SlaOptions>().Bind(configuration.GetSection(SlaOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ISlaService, SlaService>();

        // NotificationModule (C14) ต้องมีเสมอเมื่อมี SLA — notifier จริงต้องใช้ INotificationService (idempotent)
        services.AddSvmNotifications();
        services.TryAddScoped<ISlaEscalationNotifier, NotificationSlaEscalationNotifier>();
        services.TryAddScoped<SlaMonitor>();

        // AddHostedService ใช้ TryAddEnumerable — เรียกซ้ำไม่ลงทะเบียนซ้ำ
        services.AddHostedService<SlaMonitorJob>();

        return services;
    }
}
