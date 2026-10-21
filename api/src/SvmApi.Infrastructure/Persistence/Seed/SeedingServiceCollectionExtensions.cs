using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Domain.Access;

namespace SvmApi.Infrastructure.Persistence.Seed;

/// <summary>จุดลงทะเบียน/เรียกใช้ seeder ของ SVM (ใช้จาก Program.cs).</summary>
public static class SeedingServiceCollectionExtensions
{
    /// <summary>
    /// bind section <c>Seed</c> → <see cref="SeedOptions"/> และลงทะเบียน <see cref="SvmDbSeeder"/> แบบ scoped.
    /// ใช้ TryAdd สำหรับ <see cref="TimeProvider"/> และ <see cref="IPasswordHasher{TUser}"/> เป็น fallback
    /// (ไม่ทับของที่ลงทะเบียนไว้แล้ว).
    /// </summary>
    public static IServiceCollection AddSvmSeeding(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<SvmDbSeeder>();

        return services;
    }

    /// <summary>
    /// สร้าง scope แล้วรัน <see cref="SvmDbSeeder"/> เมื่อ <c>Seed:Enabled</c> = true มิฉะนั้นไม่ทำอะไร.
    /// ไม่ throw (ความผิดพลาดถูก log) เพื่อไม่ให้ API start ไม่ขึ้น; ยกเว้นการยกเลิกด้วย <paramref name="ct"/>.
    /// </summary>
    public static async Task SeedDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();

        var logger = scope.ServiceProvider
            .GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(SvmDbSeeder).FullName!);

        try
        {
            var options = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
            if (!options.Enabled)
            {
                return;
            }

            var seeder = scope.ServiceProvider.GetRequiredService<SvmDbSeeder>();
            await seeder.SeedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Database seeding failed unexpectedly; the API will continue to start.");
        }
    }
}
