using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;

namespace SvmApi.Api.Controllers;

/// <summary>
/// Auth endpoints (US-001/002/003, docs/authentication-server.md): csrf, login, refresh, me, activity, logout.
/// ทุก response เป็น no-store (middleware ใน UseSvmAuth). login/refresh ที่ล้มเหลวตอบ 401 ทั่วไป ไม่บอกสาเหตุ.
/// CSRF token ผูกกับ browser (antiforgery cookie) ไม่ผูกกับ user จึงใช้ token เดียวได้ตลอดทั้งก่อน/หลัง login.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAuthService authService,
    IAntiforgery antiforgery,
    ICurrentUser currentUser,
    IHostEnvironment environment) : ControllerBase
{
    public const string RefreshCookieName = "svm.rt";
    private const string RefreshCookiePath = "/api/v1/auth";

    /// <summary>ขอ CSRF request token (public, same origin) + ตั้ง antiforgery cookie.</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<CsrfTokenResponse> GetCsrf()
    {
        var tokens = WithAnonymousIdentity(() => antiforgery.GetAndStoreTokens(HttpContext));
        return Ok(new CsrfTokenResponse(tokens.RequestToken ?? string.Empty));
    }

    /// <summary>login ด้วย username/password (public + CSRF + rate limit).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthServiceCollectionExtensions.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCsrfValidAsync())
        {
            return InvalidCsrf();
        }

        var grant = await authService.LoginAsync(
            request.Username, request.Password, AuthHttpContext.GetRequestId(HttpContext), cancellationToken);
        if (grant is null)
        {
            return AuthProblem.ToResult(
                HttpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                "The username or password is incorrect.",
                AuthProblem.InvalidCredentials);
        }

        AppendRefreshCookie(grant);
        return Ok(grant.Response);
    }

    /// <summary>ขอ access token ใหม่ด้วย refresh cookie (หมุน credential; ไม่ต่ออายุ idle).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!await IsCsrfValidAsync())
        {
            return InvalidCsrf();
        }

        var credential = Request.Cookies[RefreshCookieName];
        var grant = string.IsNullOrEmpty(credential)
            ? null
            : await authService.RefreshAsync(credential, AuthHttpContext.GetRequestId(HttpContext), cancellationToken);
        if (grant is null)
        {
            DeleteRefreshCookie();
            return AuthProblem.ToResult(
                HttpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                "The session could not be refreshed.",
                AuthProblem.RefreshFailed);
        }

        AppendRefreshCookie(grant);
        return Ok(grant.Response);
    }

    /// <summary>ข้อมูลผู้ใช้ + deadline ของ session ปัจจุบัน (bearer).</summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<AuthState> Me()
        => AuthHttpContext.TryGetState(HttpContext, out var state) ? Ok(state) : UnauthenticatedProblem();

    /// <summary>สัญญาณ activity ของผู้ใช้ — ต่ออายุ idle หลัง validate (bearer + CSRF).</summary>
    [HttpPost("activity")]
    [Authorize]
    public async Task<IActionResult> Activity(CancellationToken cancellationToken)
    {
        if (!await IsCsrfValidAsync())
        {
            return InvalidCsrf();
        }

        if (currentUser.SessionId is not { } sessionId || currentUser.UserId is not { } userId)
        {
            return UnauthenticatedProblem();
        }

        var state = await authService.RecordActivityAsync(sessionId, userId, cancellationToken);
        return state is null ? UnauthenticatedProblem() : Ok(state);
    }

    /// <summary>ออกจากระบบ: revoke session ฝั่ง server + ลบ refresh cookie (bearer + CSRF).</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!await IsCsrfValidAsync())
        {
            return InvalidCsrf();
        }

        if (currentUser.SessionId is not { } sessionId || currentUser.UserId is not { } userId)
        {
            return UnauthenticatedProblem();
        }

        await authService.LogoutAsync(sessionId, userId, AuthHttpContext.GetRequestId(HttpContext), cancellationToken);
        DeleteRefreshCookie();
        return NoContent();
    }

    // --- helpers ---

    private bool UseSecureCookies => !AuthServiceCollectionExtensions.IsLocal(environment);

    private void AppendRefreshCookie(AuthGrant grant)
    {
        Response.Cookies.Append(RefreshCookieName, grant.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = UseSecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            IsEssential = true,
            Expires = grant.Response.SessionExpiresAt, // ไม่เกิน absolute deadline ของ session
        });
    }

    private void DeleteRefreshCookie()
    {
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = UseSecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
        });
    }

    private async Task<bool> IsCsrfValidAsync()
    {
        var original = HttpContext.User;
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
        finally
        {
            HttpContext.User = original;
        }
    }

    private T WithAnonymousIdentity<T>(Func<T> action)
    {
        var original = HttpContext.User;
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        try
        {
            return action();
        }
        finally
        {
            HttpContext.User = original;
        }
    }

    private ObjectResult UnauthenticatedProblem()
        => AuthProblem.ToResult(
            HttpContext,
            StatusCodes.Status401Unauthorized,
            "Unauthorized",
            "Authentication is required.",
            AuthProblem.Unauthenticated);

    private ObjectResult InvalidCsrf()
        => AuthProblem.ToResult(
            HttpContext,
            StatusCodes.Status400BadRequest,
            "Invalid request token",
            "The CSRF token is missing or invalid.",
            AuthProblem.InvalidCsrf);
}

/// <summary>ตอบ <c>GET /auth/csrf</c>.</summary>
public sealed record CsrfTokenResponse(string Token);

/// <summary>request ของ <c>POST /auth/login</c>.</summary>
public sealed class LoginRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}
