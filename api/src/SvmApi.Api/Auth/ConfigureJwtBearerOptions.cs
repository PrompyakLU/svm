using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SvmApi.Application.Auth;

namespace SvmApi.Api.Auth;

/// <summary>
/// ตั้งค่า JWT bearer (US-001/003): ตรวจ signature, algorithm, issuer, audience, lifetime
/// แล้ว <c>OnTokenValidated</c> ตรวจ sub/sid กับ DB ทุก request และโหลดสิทธิ์ล่าสุดจาก DB.
/// </summary>
public sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions) : IConfigureNamedOptions<JwtBearerOptions>
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(15);

    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var jwt = jwtOptions.Value;

        options.MapInboundClaims = false; // คง "sub"/"sid" ตามที่ออก
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = OnTokenValidatedAsync,
            OnAuthenticationFailed = OnAuthenticationFailed,
        };
    }

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var subject = principal?.FindFirst(JwtAuthClaims.Subject)?.Value;
        var sessionClaim = principal?.FindFirst(JwtAuthClaims.SessionId)?.Value
                           ?? principal?.FindFirst(ClaimTypes.Sid)?.Value;

        if (principal is null
            || !Guid.TryParse(subject, out var userId)
            || !Guid.TryParse(sessionClaim, out var sessionId))
        {
            context.Fail("Token is missing required claims.");
            return;
        }

        var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
        var state = await authService.ValidateAsync(sessionId, userId, context.HttpContext.RequestAborted);
        if (state is null)
        {
            // session ถูก revoke/หมดอายุ/idle หรือบัญชีถูกปิด — ไม่บอกสาเหตุ
            context.Fail("Session is not valid.");
            return;
        }

        AuthHttpContext.SetState(context.HttpContext, state, sessionId);

        // identity เสริมจากข้อมูลล่าสุดใน DB (ไม่ใช่จาก token)
        var claims = new List<Claim> { new(ClaimTypes.Name, state.User.Username) };
        claims.AddRange(state.User.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        principal.AddIdentity(new ClaimsIdentity(claims, "svm-db", ClaimTypes.Name, ClaimTypes.Role));
    }

    private static Task OnAuthenticationFailed(AuthenticationFailedContext context)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("SvmApi.Api.Auth.Jwt");
        // log เฉพาะชนิด exception (ไม่ log token)
        logger.LogInformation(
            "auth.token.rejected requestId={RequestId} reason={Reason}",
            AuthHttpContext.GetRequestId(context.HttpContext),
            context.Exception.GetType().Name);
        return Task.CompletedTask;
    }
}
