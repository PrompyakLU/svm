using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SvmApi.Application.Auth;
using SvmApi.Infrastructure.Auth;

namespace SvmApi.Api.Auth;

/// <summary>
/// ประกอบ AuthModule (C1 / US-001..003) เข้ากับ ASP.NET Core:
/// JWT bearer, permission policies (deny by default), antiforgery, rate limit ของ login.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>ชื่อ rate-limit policy ของ <c>POST /auth/login</c>.</summary>
    public const string LoginRateLimitPolicy = "svm-auth-login";

    /// <summary>header ที่รับ CSRF request token.</summary>
    public const string CsrfHeaderName = "X-XSRF-TOKEN";

    public const string AuthRoutePrefix = "/api/v1/auth";

    private const int DefaultLoginPermitLimit = 10;
    private const int DefaultLoginWindowSeconds = 60;

    public static IServiceCollection AddSvmAuth(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSvmAuthInfrastructure();
        services.AddHttpContextAccessor();

        // options + validation ตอน startup (signing key ต้อง >= 32 ไบต์ — ข้อความไม่พิมพ์ค่า key)
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>());

        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // Authentication: JWT bearer (ตั้งค่าผ่าน ConfigureJwtBearerOptions เพื่ออ่าน options หลัง config พร้อม)
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.ConfigureOptions<ConfigureJwtBearerOptions>();

        // Authorization: deny by default (FallbackPolicy) + policy แบบ perm:<code>
        services.AddAuthorization(options =>
        {
            var authenticated = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.DefaultPolicy = authenticated;
            options.FallbackPolicy = authenticated;
        });
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SvmAuthorizationResultHandler>();

        // CSRF: antiforgery ผ่าน header X-XSRF-TOKEN (SameSite เป็นชั้นเสริม ไม่ใช่ตัวแทน)
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeaderName;
            options.SuppressXFrameOptionsHeader = true;
            options.Cookie.Name = "svm.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = AuthRoutePrefix;
            options.Cookie.SecurePolicy = IsLocal(environment)
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        // Rate limit /auth/login ต่อ IP (fixed window)
        var permitLimit = Math.Max(1,
            configuration.GetValue("Auth:LoginRateLimit:PermitLimit", DefaultLoginPermitLimit));
        var windowSeconds = Math.Max(1,
            configuration.GetValue("Auth:LoginRateLimit:WindowSeconds", DefaultLoginWindowSeconds));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                var problem = AuthProblem.Create(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "Too many requests",
                    "Too many attempts. Try again later.",
                    AuthProblem.RateLimited);
                await response.WriteAsJsonAsync(
                    problem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// ลำดับ pipeline: no-store (auth routes) -> rate limiter -> UseAuthentication -> UseAuthorization.
    /// เรียกหลัง exception handler / https redirection ใน Program.cs.
    /// </summary>
    public static WebApplication UseSvmAuth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(AuthRoutePrefix, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.OnStarting(static state =>
                {
                    var response = ((HttpContext)state).Response;
                    response.Headers.CacheControl = "no-store";
                    response.Headers.Pragma = "no-cache";
                    return Task.CompletedTask;
                }, context);
            }

            await next();
        });

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>local Development/Testing: cookie ไม่บังคับ Secure.</summary>
    public static bool IsLocal(IHostEnvironment environment)
        => environment.IsDevelopment() || environment.IsEnvironment("Testing");
}
