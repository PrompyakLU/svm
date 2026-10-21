using SvmApi.Infrastructure.Persistence.Seed;

namespace SvmApi.Tests.Auth;

/// <summary>
/// ทดสอบส่วน pure ของ stage "role-grants" (ตาราง patch + seed ของ SYSTEM_ADMIN).
/// ไม่มี EF provider สำหรับทดสอบ (SQLite/in-memory) ในโปรเจกต์ทดสอบ จึงไม่ได้ทดสอบ DB round-trip.
/// </summary>
public sealed class RoleGrantPatchesTests
{
    private static readonly string[] FindingCodes =
    [
        PermissionCatalog.FindingImport,
        PermissionCatalog.FindingCreate,
        PermissionCatalog.FindingDedup,
    ];

    [Fact]
    public void SystemAdminPatchContainsAllFindingPermissionsOnly()
    {
        Assert.True(SeedRoleGrants.Patches.TryGetValue(SeedRoleCodes.SystemAdmin, out var patch));
        Assert.Equal(FindingCodes.OrderBy(x => x), patch.OrderBy(x => x));
    }

    [Fact]
    public void SystemAdminSeedContainsFindingPermissionsAndNoApproveOrAuditExport()
    {
        var seeded = SeedRoleGrants.SeededPermissions(SeedRoleCodes.SystemAdmin);

        Assert.All(FindingCodes, code => Assert.Contains(code, seeded));
        Assert.DoesNotContain(PermissionCatalog.InstanceApprove, seeded);
        Assert.DoesNotContain(PermissionCatalog.AuditExport, seeded);
    }

    [Fact]
    public void SystemAdminFindingPermissionsMatchVulnMgmtOfficer()
    {
        var admin = SeedRoleGrants.SeededPermissions(SeedRoleCodes.SystemAdmin);
        var officer = SeedRoleGrants.SeededPermissions(SeedRoleCodes.VulnMgmtOfficer);

        var adminFindings = admin.Where(c => c.StartsWith("finding.", StringComparison.Ordinal)).OrderBy(x => x);
        var officerFindings = officer.Where(c => c.StartsWith("finding.", StringComparison.Ordinal)).OrderBy(x => x);
        Assert.Equal(officerFindings, adminFindings);
    }

    [Fact]
    public void PatchesNeverGrantApproveOrAuditExport()
    {
        var all = SeedRoleGrants.Patches.Values.SelectMany(x => x).ToList();
        Assert.DoesNotContain(PermissionCatalog.InstanceApprove, all);
        Assert.DoesNotContain(PermissionCatalog.AuditExport, all);
    }

    [Fact]
    public void PatchPermissionCodesExistInCatalog()
    {
        var catalog = PermissionCatalog.All.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(SeedRoleGrants.Patches.Values.SelectMany(x => x), code => Assert.Contains(code, catalog));
    }
}
