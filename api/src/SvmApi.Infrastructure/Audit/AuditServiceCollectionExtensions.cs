using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SvmApi.Application.Audit;

namespace SvmApi.Infrastructure.Audit;

/// <summary>ลงทะเบียน AuditModule (C13 · FR-68, BR-33, NFR-20/21).</summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// ลงทะเบียน <see cref="IAuditService"/> (scoped), <see cref="TimeProvider"/>,
    /// <see cref="AuditLogImmutabilityInterceptor"/> (singleton) และ <see cref="AuditIntegrityJob"/>.
    /// ต้องเพิ่ม interceptor เข้า DbContext ด้วย:
    /// <c>AddDbContext&lt;SvmDbContext&gt;((sp, o) =&gt; o.AddInterceptors(sp.GetRequiredService&lt;AuditLogImmutabilityInterceptor&gt;()))</c>.
    /// </summary>
    public static IServiceCollection AddSvmAudit(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAuditService, AuditService>();
        services.TryAddSingleton<AuditLogImmutabilityInterceptor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AuditIntegrityJob>());

        return services;
    }
}
