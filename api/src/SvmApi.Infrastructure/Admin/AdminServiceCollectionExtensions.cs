using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SvmApi.Application.Admin;
using SvmApi.Infrastructure.Reference;

namespace SvmApi.Infrastructure.Admin;

public static class AdminServiceCollectionExtensions
{
    public static IServiceCollection AddSvmAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ReferenceSnapshotCache>();
        services.TryAddScoped<IAdminService, AdminService>();
        services.TryAddSingleton<SlaRecalculationQueue>();
        services.TryAddSingleton<ISlaRecalculationScheduler>(sp => sp.GetRequiredService<SlaRecalculationQueue>());
        services.AddHostedService<SlaRecalculationWorker>();
        return services;
    }
}
