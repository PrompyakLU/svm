using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SvmApi.Infrastructure.Configuration;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure;

/// <summary>
/// จุดลงทะเบียน service ของชั้น Infrastructure (EF Core, repositories, external adapters).
/// Wave 1: วาง seam + bind IntegrationOptions. Wave 3: เติม SvmDbContext (SQL Server).
/// Wave 4 เลือก mock/real adapter ตาม <see cref="IntegrationOptions.UseMock"/>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddSvmInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<IntegrationOptions>()
            .Bind(configuration.GetSection(IntegrationOptions.SectionName))
            .ValidateOnStart();

        // Connection string มาจาก ConnectionStrings:Svm — ตั้งผ่าน env var ConnectionStrings__Svm
        // ใน Development/Staging/Production (ห้าม commit secret จริง — ดู appsettings.*.json comment)
        var connectionString = configuration.GetConnectionString("Svm")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:Svm'. Set via appsettings or env var ConnectionStrings__Svm.");

        services.AddDbContext<SvmDbContext>((sp, options) =>
            options
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 3))
                .AddInterceptors(sp.GetRequiredService<SvmApi.Infrastructure.Audit.AuditLogImmutabilityInterceptor>()));

        services.AddScoped<SvmApi.Application.Common.IUnitOfWork, EfUnitOfWork>();

        // TODO (Wave 4): register mock vs real external adapters by IntegrationOptions.UseMock
        return services;
    }
}
