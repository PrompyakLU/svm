using System.Diagnostics.CodeAnalysis;
using SvmApi.Api.Middleware;
using SvmApi.Application.Auth;

namespace SvmApi.Api.Auth;

/// <summary>
/// ที่เก็บสถานะ auth ที่ validate กับ DB แล้วใน <c>HttpContext.Items</c> (ตั้งใน JWT <c>OnTokenValidated</c>)
/// เพื่อให้ <see cref="CurrentUser"/> / controller ไม่ต้อง query ซ้ำ.
/// </summary>
public static class AuthHttpContext
{
    private const string StateKey = "svm.auth.state";
    private const string SessionKey = "svm.auth.session";
    private const int MaxRequestIdLength = 64;

    public static void SetState(HttpContext context, AuthState state, Guid sessionId)
    {
        context.Items[StateKey] = state;
        context.Items[SessionKey] = sessionId;
    }

    public static bool TryGetState(HttpContext context, [NotNullWhen(true)] out AuthState? state)
    {
        state = context.Items.TryGetValue(StateKey, out var value) ? value as AuthState : null;
        return state is not null;
    }

    public static Guid? GetSessionId(HttpContext context)
        => context.Items.TryGetValue(SessionKey, out var value) && value is Guid id ? id : null;

    /// <summary>request id สำหรับ audit/log (ตัดความยาว + กรองอักขระ เพราะรับจาก header ได้).</summary>
    public static string GetRequestId(HttpContext context)
    {
        var raw = context.Response.Headers[RequestIdMiddleware.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = context.TraceIdentifier;
        }

        var chars = raw.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')
            .Take(MaxRequestIdLength)
            .ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }
}
