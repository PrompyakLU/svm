using Microsoft.AspNetCore.Mvc;

namespace SvmApi.Api.Auth;

/// <summary>
/// สร้าง ProblemDetails ของ AuthModule ให้รูปแบบเดียวกับ AuditController (api-spec "Error Format"):
/// มี extension <c>code</c> + <c>requestId</c> เสมอ และข้อความทั่วไป (ไม่เปิดเผยสาเหตุเชิงเทคนิค).
/// </summary>
public static class AuthProblem
{
    public const string Unauthenticated = "AUTH_UNAUTHENTICATED";
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string RefreshFailed = "AUTH_REFRESH_FAILED";
    public const string Forbidden = "AUTH_FORBIDDEN";
    public const string InvalidCsrf = "AUTH_INVALID_CSRF";
    public const string RateLimited = "RATE_LIMITED";

    public static ProblemDetails Create(HttpContext context, int status, string title, string detail, string code)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        problem.Extensions["requestId"] = AuthHttpContext.GetRequestId(context);
        return problem;
    }

    public static ObjectResult ToResult(HttpContext context, int status, string title, string detail, string code)
        => new(Create(context, status, title, detail, code))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
}
