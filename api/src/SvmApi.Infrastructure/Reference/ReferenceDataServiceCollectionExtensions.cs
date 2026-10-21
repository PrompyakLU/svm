using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SvmApi.Application.Reference;

namespace SvmApi.Infrastructure.Reference;

/// <summary>ลงทะเบียน ReferenceData (C5 · US-013/014).</summary>
public static class ReferenceDataServiceCollectionExtensions
{
    public static IServiceCollection AddSvmReferenceData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ReferenceSnapshotCache>();
        services.TryAddScoped<IReferenceDataService, ReferenceDataService>();

        return services;
    }
}
