using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Intake;
using SvmApi.Application.Verification;
using SvmApi.Infrastructure.Configuration;
using SvmApi.Infrastructure.Integration.Mock;
using SvmApi.Infrastructure.Notifications;
using SvmApi.Infrastructure.Workflow;

namespace SvmApi.Infrastructure.Verification;

/// <summary>
/// ลงทะเบียน VerificationModule (C10): <see cref="IVerificationService"/> + สิ่งที่ต้องใช้ (WorkflowModule, NotificationModule,
/// scanner adapter — mock ตาม <c>Integration:UseMock</c>). idempotent (TryAdd) — เรียกซ้ำ/ร่วมกับ <c>AddSvmIntake</c> ได้.
/// </summary>
public static class VerificationServiceCollectionExtensions
{
    public static IServiceCollection AddSvmVerification(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSvmWorkflow();
        services.AddSvmNotifications();

        services.TryAddScoped<IScannerAdapter>(sp =>
            sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.UseMock
                ? new MockScannerAdapter()
                : new UnavailableScannerAdapter());

        services.TryAddScoped<IVerificationService, VerificationService>();

        return services;
    }
}
