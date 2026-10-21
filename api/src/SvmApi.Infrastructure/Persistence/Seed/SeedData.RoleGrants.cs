namespace SvmApi.Infrastructure.Persistence.Seed;

internal static partial class SeedData
{
    /// <summary>
    /// ตาราง patch แบบ additive สำหรับ DB ที่มีอยู่แล้ว (role code -> permission codes).
    /// stage "role-grants" จะเพิ่มเฉพาะ role-permission link ที่ยังไม่มี — ไม่ลบ/ไม่แก้ row เดิม
    /// และข้ามอย่างเงียบ ๆ ถ้าไม่พบ role หรือ permission.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> RoleGrantPatches { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [SeedRoleCodes.SystemAdmin] =
            [
                PermissionCatalog.FindingImport,
                PermissionCatalog.FindingCreate,
                PermissionCatalog.FindingDedup,
            ],
        };
}

/// <summary>
/// มุมมอง public (read-only) ของข้อมูล seed ด้านสิทธิ์ สำหรับการทดสอบข้าม project
/// (<c>SeedData</c> เป็น internal).
/// </summary>
public static class SeedRoleGrants
{
    /// <summary>role code -> permission codes ที่ stage "role-grants" จะเพิ่มให้ role ที่มีอยู่แล้ว.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Patches => SeedData.RoleGrantPatches;

    /// <summary>permission codes ที่ seed ให้ role ตอนสร้างใหม่ (ว่างถ้าไม่พบ role).</summary>
    public static IReadOnlyList<string> SeededPermissions(string roleCode)
        => SeedData.Roles
            .FirstOrDefault(r => string.Equals(r.Code, roleCode, StringComparison.OrdinalIgnoreCase))
            ?.Permissions ?? [];
}
