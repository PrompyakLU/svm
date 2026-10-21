using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Intake;
using SvmApi.Infrastructure.Configuration;
using SvmApi.Infrastructure.Integration.Mock;
using SvmApi.Infrastructure.Risk;
using SvmApi.Infrastructure.Workflow;

namespace SvmApi.Infrastructure.Intake;

/// <summary>ลงทะเบียน IntakeModule (C3) + DataQualityService (C4) + scanner adapter (mock ตาม <c>Integration:UseMock</c>).</summary>
public static class IntakeServiceCollectionExtensions
{
    public static IServiceCollection AddSvmIntake(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        // ค่า default ปลอดภัย (5 MB / 5,000 แถว); ค่าที่ตั้งเกิน hard cap ถูก clamp ใน IntakeOptions.Effective*
        services
            .AddOptions<IntakeOptions>()
            .Bind(configuration.GetSection(IntakeOptions.SectionName))
            .Validate(o => o.MaxFileBytes > 0 && o.MaxRows > 0 && o.MaxReportedErrors > 0, "Intake limits must be positive.")
            .ValidateOnStart();

        services.TryAddScoped<IScannerAdapter>(sp =>
            sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.UseMock
                ? new MockScannerAdapter()
                : new UnavailableScannerAdapter());

        // คะแนน risk เริ่มต้นของ instance ใหม่ (4.4)
        services.AddSvmRisk();

        // BR-13: recurrence reopen ผ่าน IWorkflowService (idempotent)
        services.AddSvmWorkflow();

        services.TryAddScoped<DataQualityService>();
        services.TryAddScoped<IDataQualityService>(sp => sp.GetRequiredService<DataQualityService>());
        services.TryAddScoped<IIntakeService, IntakeService>();

        return services;
    }
}
