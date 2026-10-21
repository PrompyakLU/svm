using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SvmApi.Application.Auth;
using SvmApi.Domain.Access;

namespace SvmApi.Infrastructure.Auth;

/// <summary>ลงทะเบียน service ฝั่ง Infrastructure ของ AuthModule (C1) — เรียกซ้ำได้อย่างปลอดภัย.</summary>
public static class AuthInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSvmAuthInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.TryAddScoped<IAuthProvider, LocalAuthProvider>();
        services.TryAddScoped<IAuthService, AuthService>();

        return services;
    }
}
