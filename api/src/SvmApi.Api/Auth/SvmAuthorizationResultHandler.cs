using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using SvmApi.Application.Auth;

namespace SvmApi.Api.Auth;

/// <summary>
/// ผลลัพธ์ authorization เป็น ProblemDetails (US-001/002): 401 = ยังไม่ authenticate, 403 = ไม่มีสิทธิ์
/// (403 บันทึก denied-access ผ่าน <see cref="IAuthService.RecordDeniedAsync"/> โดยไม่ทำให้ response พัง).
/// ข้อความทั่วไป ไม่บอกรายละเอียดภายใน.
/// </summary>
public sealed class SvmAuthorizationResultHandler(ILogger<SvmAuthorizationResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized",
                "Authentication is required.", AuthProblem.Unauthenticated);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await RecordDeniedAsync(context);
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Forbidden",
                "You do not have permission to perform this action.", AuthProblem.Forbidden);
            return;
        }

        await next(context);
    }

    private async Task RecordDeniedAsync(HttpContext context)
    {
        try
        {
            var services = context.RequestServices;
            var userId = services.GetRequiredService<ICurrentUser>().UserId;
            var resource = $"{context.Request.Method} {context.Request.Path}"; // ไม่รวม query string
            await services.GetRequiredService<IAuthService>().RecordDeniedAsync(
                userId, resource, AuthHttpContext.GetRequestId(context), context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // audit ล้มเหลวต้องไม่เปลี่ยน 403 เป็น 500 — log ชนิด exception เท่านั้น
            logger.LogError(
                "auth.access.denied.audit_failed requestId={RequestId} reason={Reason}",
                AuthHttpContext.GetRequestId(context), ex.GetType().Name);
        }
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string title, string detail, string code)
    {
        context.Response.StatusCode = status;
        var problem = AuthProblem.Create(context, status, title, detail, code);
        return context.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
