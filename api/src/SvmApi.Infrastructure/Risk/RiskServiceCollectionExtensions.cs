using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SvmApi.Application.Risk;

namespace SvmApi.Infrastructure.Risk;

/// <summary>ลงทะเบียน RiskScoringEngine (C6). idempotent (TryAdd) — Intake เรียกซ้ำได้เพราะใช้คำนวณคะแนนเริ่มต้น.</summary>
public static class RiskServiceCollectionExtensions
{
    public static IServiceCollection AddSvmRisk(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<RiskScoringService>();
        services.TryAddScoped<IRiskScoringService>(sp => sp.GetRequiredService<RiskScoringService>());

        return services;
    }
}
