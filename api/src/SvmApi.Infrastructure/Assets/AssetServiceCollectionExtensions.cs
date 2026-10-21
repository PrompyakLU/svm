using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Assets;
using SvmApi.Infrastructure.Configuration;
using SvmApi.Infrastructure.Integration.Mock;

namespace SvmApi.Infrastructure.Assets;

/// <summary>ลงทะเบียน AssetModule (C2 · US-004/005/006) และเลือก CMDB adapter ตาม <c>Integration:UseMock</c>.</summary>
public static class AssetServiceCollectionExtensions
{
    public static IServiceCollection AddSvmAssets(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services
            .AddOptions<CmdbMockOptions>()
            .Bind(configuration.GetSection(CmdbMockOptions.SectionName));

        // UseMock=true (default) -> mock อ่าน seed; UseMock=false -> ยังไม่มี real adapter ใน workshop
        // จึงใช้ UnavailableCmdbAdapter (รายงาน integration error ไม่เรียกระบบจริง). IntegrationOptions ถูก bind ใน AddSvmInfrastructure.
        services.TryAddScoped<ICmdbAdapter>(sp =>
            sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.UseMock
                ? ActivatorUtilities.CreateInstance<MockCmdbAdapter>(sp)
                : ActivatorUtilities.CreateInstance<UnavailableCmdbAdapter>(sp));

        services.TryAddScoped<IAssetService, AssetService>();

        return services;
    }
}
