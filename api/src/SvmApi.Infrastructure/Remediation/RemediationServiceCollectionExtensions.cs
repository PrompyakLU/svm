using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Remediation;
using SvmApi.Infrastructure.Configuration;
using SvmApi.Infrastructure.Integration.Mock;
using SvmApi.Infrastructure.Notifications;

namespace SvmApi.Infrastructure.Remediation;

/// <summary>
/// ลงทะเบียน AssignmentRemediationModule (C8): assignment, remediation task, evidence (LocalFileStorage + malware scanner mock), comment
/// และ NotificationModule (C14) ที่ใช้ร่วมกัน. config: section <c>Evidence</c> (<c>MaxFileBytes</c>, <c>StorageRoot</c>).
/// </summary>
public static class RemediationServiceCollectionExtensions
{
    public static IServiceCollection AddSvmRemediation(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services
            .AddOptions<EvidenceOptions>()
            .Bind(configuration.GetSection(EvidenceOptions.SectionName))
            .Validate(o => o.MaxFileBytes > 0, "Evidence:MaxFileBytes must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton<IFileStorage, LocalFileStorage>();

        services.TryAddScoped<IMalwareScanner>(sp =>
            sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.UseMock
                ? new MockMalwareScanner()
                : new UnavailableMalwareScanner());

        services.AddSvmNotifications();

        services.TryAddScoped<IAssignmentService, AssignmentService>();
        services.TryAddScoped<IRemediationTaskService, RemediationTaskService>();
        services.TryAddScoped<IEvidenceService, EvidenceService>();
        services.TryAddScoped<ICommentService, CommentService>();

        return services;
    }
}
