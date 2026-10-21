using SvmApi.Application.Auth;

namespace SvmApi.Api.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> จาก request ปัจจุบัน (C1 / US-001..003): roles/permissions/scopes มาจาก DB
/// ที่โหลดใน <c>OnTokenValidated</c> ไม่ใช่จาก claim ใน JWT. ไม่ authenticated = deny by default.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private AuthState? State
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null || context.User.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return AuthHttpContext.TryGetState(context, out var state) ? state : null;
        }
    }

    public bool IsAuthenticated => State is not null;

    public Guid? UserId => State?.User.Id;

    public Guid? SessionId
        => State is null || accessor.HttpContext is null ? null : AuthHttpContext.GetSessionId(accessor.HttpContext);

    public string? Username => State?.User.Username;

    public IReadOnlyCollection<string> Roles => State?.User.Roles ?? [];

    public IReadOnlyCollection<string> Permissions => State?.User.Permissions ?? [];

    public IReadOnlyCollection<AuthScope> Scopes => State?.User.Scopes ?? [];

    public bool HasPermission(string permission)
        => State is { } state && PermissionMatcher.HasPermission(state.User.Permissions, permission);
}
