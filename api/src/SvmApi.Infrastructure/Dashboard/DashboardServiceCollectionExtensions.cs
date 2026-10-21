using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SvmApi.Application.Dashboard;
using SvmApi.Application.Sla;

namespace SvmApi.Infrastructure.Dashboard;

public static class DashboardServiceCollectionExtensions
{
    public static IServiceCollection AddSvmDashboard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<SlaOptions>();
        services.TryAddScoped<IInstanceQueryService, InstanceQueryService>();
        services.TryAddScoped<IDashboardService, DashboardService>();
        services.TryAddScoped<IBusinessUnitQueryService, BusinessUnitQueryService>();
        return services;
    }
}
