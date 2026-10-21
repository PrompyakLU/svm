namespace SvmApi.Application.Auth;

/// <summary>
/// ผู้ใช้ของ request ปัจจุบัน — สร้างจากสถานะล่าสุดในฐานข้อมูล (ไม่ใช่จาก request input).
/// Implement ใน Api layer (อ่านจาก HttpContext) · C1 / US-001..003.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? SessionId { get; }
    string? Username { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<string> Permissions { get; }
    IReadOnlyCollection<AuthScope> Scopes { get; }

    /// <summary>ตรวจสิทธิ์ด้วย <see cref="PermissionMatcher"/> (deny by default).</summary>
    bool HasPermission(string permission);
}

/// <summary>ชื่อ policy ของ ASP.NET Core authorization = "perm:" + permission code.</summary>
public static class PermissionPolicies
{
    public const string Prefix = "perm:";

    public static string For(string permission) => Prefix + permission;

    public static bool TryGetPermission(string policyName, out string permission)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal) && policyName.Length > Prefix.Length)
        {
            permission = policyName[Prefix.Length..];
            return true;
        }

        permission = string.Empty;
        return false;
    }
}

/// <summary>
/// กติกา match สิทธิ์ (deny by default): exact match หรือ wildcard ระดับ family เช่น
/// <c>admin.*</c> ตรงกับ <c>admin.users.read</c> เท่านั้น — ไม่มี wildcard ระดับ global
/// (<c>*</c> เดี่ยว ๆ ไม่ match อะไรเลย).
/// </summary>
public static class PermissionMatcher
{
    public static bool Matches(string grant, string required)
    {
        if (string.IsNullOrWhiteSpace(grant) || string.IsNullOrWhiteSpace(required))
        {
            return false;
        }

        if (string.Equals(grant, required, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (grant.Length > 2 && grant.EndsWith(".*", StringComparison.Ordinal))
        {
            var family = grant[..^1]; // เก็บจุดท้ายไว้ เช่น "admin."
            return required.StartsWith(family, StringComparison.OrdinalIgnoreCase)
                   && required.Length > family.Length;
        }

        return false;
    }

    public static bool HasPermission(IEnumerable<string> grants, string required)
        => grants.Any(g => Matches(g, required));
}
