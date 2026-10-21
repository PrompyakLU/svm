using SvmApi.Domain.Access;

namespace SvmApi.Infrastructure.Persistence.Seed;

/// <summary>รหัส role หลัก 9 บทบาท (config_role.code) — UPPER_SNAKE ตาม personas.md.</summary>
public static class SeedRoleCodes
{
    public const string SystemAdmin = "SYSTEM_ADMIN";
    public const string CyberAdmin = "CYBER_ADMIN";
    public const string VulnMgmtOfficer = "VULN_MGMT_OFFICER";
    public const string SecurityAnalyst = "SECURITY_ANALYST";
    public const string AssetOwner = "ASSET_OWNER";
    public const string RemediationOwner = "REMEDIATION_OWNER";
    public const string RiskApprover = "RISK_APPROVER";
    public const string Auditor = "AUDITOR";
    public const string Executive = "EXECUTIVE";
}

internal static partial class SeedData
{
    internal sealed record RoleSeed(
        string Code,
        string Name,
        string Description,
        IReadOnlyList<string> Permissions);

    internal sealed record ScopeSeed(ScopeType Type, string Value);

    internal sealed record DemoUserSeed(
        string Username,
        string DisplayName,
        string Email,
        string RoleCode,
        IReadOnlyList<ScopeSeed> Scopes);

    /// <summary>
    /// 9 role หลัก (IsSystem = true) พร้อม permission แบบ least privilege (NFR-11/12/13).
    /// ข้อจำกัดตาม personas.md:
    /// - SYSTEM_ADMIN: ตั้งค่าได้ และมี <c>finding.*</c> ครบ (import/create/dedup) เท่ากับ VULN_MGMT_OFFICER; แต่ไม่มี <c>instance.approve</c> และไม่มี <c>audit.export</c>; ไม่มีสิทธิ์แก้/ลบ audit (ไม่มี endpoint เขียน audit).
    ///   role ที่มีอยู่แล้วใน DB ได้รับ finding.* ผ่าน stage "role-grants" (<see cref="RoleGrantPatches"/>).
    /// - CYBER_ADMIN: กำหนด risk/SLA/workflow แต่ไม่จัดการ user และไม่อนุมัติ.
    /// - VULN_MGMT_OFFICER: ไม่มี <c>instance.approve</c> (ห้ามอนุมัติ Risk Acceptance ที่ตนยื่น).
    /// - REMEDIATION_OWNER: ส่ง Pending Verification ได้ แต่ไม่มี <c>verification.record</c> / <c>instance.approve</c> (ปิดงานตนเองไม่ได้).
    /// - RISK_APPROVER: อนุมัติได้ แต่ไม่แก้ risk/assign/verify. SoD "ผู้ยื่น ≠ ผู้อนุมัติ" บังคับใน business logic.
    /// - AUDITOR: read-only + <c>audit.read</c>/<c>audit.export</c>; ไม่มี permission เขียนใด ๆ.
    /// - EXECUTIVE: dashboard.exec + อ่านเพื่อ drill-down เท่านั้น.
    /// ไม่มี wildcard global และไม่ใช้ <c>admin.*</c> (ระบุ code แบบ explicit).
    /// </summary>
    internal static IReadOnlyList<RoleSeed> Roles { get; } =
    [
        new(SeedRoleCodes.SystemAdmin, "System Administrator",
            "ผู้ดูแลระบบ: จัดการ user/role/master data/workflow/SLA และตรวจ health; ไม่อนุมัติ business decision และแก้ audit ไม่ได้",
        [
            PermissionCatalog.AssetRead, PermissionCatalog.AssetWrite, PermissionCatalog.AssetSync,
            PermissionCatalog.FindingImport, PermissionCatalog.FindingCreate, PermissionCatalog.FindingDedup,
            PermissionCatalog.ReferenceRead, PermissionCatalog.DashboardOps,
            PermissionCatalog.AdminMasterData, PermissionCatalog.AdminWorkflow, PermissionCatalog.AdminSla,
            PermissionCatalog.AdminRisk, PermissionCatalog.AdminAssignment, PermissionCatalog.AdminUsers,
            PermissionCatalog.AuditRead,
        ]),

        new(SeedRoleCodes.CyberAdmin, "Cybersecurity Administrator",
            "กำหนดมาตรฐาน risk model/SLA/workflow/assignment; เปลี่ยนนโยบายต้องผ่าน change control",
        [
            PermissionCatalog.AssetRead, PermissionCatalog.ReferenceRead,
            PermissionCatalog.InstanceRead, PermissionCatalog.InstanceRiskAdjust,
            PermissionCatalog.DashboardOps,
            PermissionCatalog.AdminMasterData, PermissionCatalog.AdminWorkflow, PermissionCatalog.AdminSla,
            PermissionCatalog.AdminRisk, PermissionCatalog.AdminAssignment,
        ]),

        new(SeedRoleCodes.VulnMgmtOfficer, "Vulnerability Management Officer",
            "เจ้าของกระบวนการหลัก: คัดกรอง/ยืนยัน finding ตรวจ duplicate มอบหมาย ติดตาม SLA ปิด/เปิดกลับ; ไม่อนุมัติ Risk Acceptance",
        [
            PermissionCatalog.AssetRead, PermissionCatalog.AssetWrite, PermissionCatalog.AssetCriticality,
            PermissionCatalog.FindingImport, PermissionCatalog.FindingCreate, PermissionCatalog.FindingDedup,
            PermissionCatalog.ReferenceRead,
            PermissionCatalog.InstanceRead, PermissionCatalog.InstanceRiskAdjust,
            PermissionCatalog.InstanceTransition, PermissionCatalog.InstanceAssign,
            PermissionCatalog.TaskRead, PermissionCatalog.TaskUpdate,
            PermissionCatalog.EvidenceUpload, PermissionCatalog.CommentWrite,
            PermissionCatalog.VerificationSubmit, PermissionCatalog.VerificationRescan, PermissionCatalog.VerificationRecord,
            PermissionCatalog.DashboardOps,
        ]),

        new(SeedRoleCodes.SecurityAnalyst, "Security Analyst",
            "วิเคราะห์เชิงเทคนิค ยืนยันผล ทำ verification/rescan; Critical ต้อง independent verification",
        [
            PermissionCatalog.AssetRead,
            PermissionCatalog.FindingImport, PermissionCatalog.FindingCreate, PermissionCatalog.FindingDedup,
            PermissionCatalog.ReferenceRead,
            PermissionCatalog.InstanceRead, PermissionCatalog.InstanceRiskAdjust, PermissionCatalog.InstanceTransition,
            PermissionCatalog.TaskRead, PermissionCatalog.EvidenceUpload, PermissionCatalog.CommentWrite,
            PermissionCatalog.VerificationRescan, PermissionCatalog.VerificationRecord,
            PermissionCatalog.DashboardOps,
        ]),

        new(SeedRoleCodes.AssetOwner, "Asset / System Owner",
            "เจ้าของทรัพย์สิน/ระบบ: ดูช่องโหว่ใน scope ของตน กำหนด criticality มอบหมายทีมแก้ไข; ยื่นได้แต่ไม่อนุมัติของตนเอง",
        [
            PermissionCatalog.AssetRead, PermissionCatalog.AssetCriticality,
            PermissionCatalog.InstanceRead, PermissionCatalog.InstanceAssign, PermissionCatalog.InstanceTransition,
            PermissionCatalog.TaskRead, PermissionCatalog.CommentWrite,
            PermissionCatalog.DashboardOps,
        ]),

        new(SeedRoleCodes.RemediationOwner, "Remediation Owner",
            "ผู้รับผิดชอบการแก้ไข: อัปเดตแผน แนบหลักฐาน ส่ง Pending Verification; ปิด/อนุมัติงานตนเองไม่ได้",
        [
            PermissionCatalog.AssetRead,
            PermissionCatalog.InstanceRead, PermissionCatalog.InstanceTransition,
            PermissionCatalog.TaskRead, PermissionCatalog.TaskUpdate,
            PermissionCatalog.EvidenceUpload, PermissionCatalog.CommentWrite,
            PermissionCatalog.VerificationSubmit,
            PermissionCatalog.DashboardOps,
        ]),

        new(SeedRoleCodes.RiskApprover, "Risk Owner / Approver",
            "ผู้อนุมัติ False Positive / Risk Accepted / Exception ตาม approval matrix; ห้ามอนุมัติคำขอของตนเอง",
        [
            PermissionCatalog.AssetRead, PermissionCatalog.ReferenceRead,
            PermissionCatalog.InstanceRead, PermissionCatalog.InstanceTransition, PermissionCatalog.InstanceApprove,
            PermissionCatalog.TaskRead, PermissionCatalog.CommentWrite,
            PermissionCatalog.DashboardOps,
        ]),

        new(SeedRoleCodes.Auditor, "Auditor / Compliance Officer",
            "ผู้ตรวจสอบ: read-only ตรวจ audit trail/SLA/approval; export ต้องได้รับอนุญาตและถูกบันทึก",
        [
            PermissionCatalog.AssetRead, PermissionCatalog.ReferenceRead,
            PermissionCatalog.InstanceRead, PermissionCatalog.TaskRead,
            PermissionCatalog.DashboardOps,
            PermissionCatalog.AuditRead, PermissionCatalog.AuditExport,
        ]),

        new(SeedRoleCodes.Executive, "Management / Executive",
            "ผู้บริหาร: executive dashboard และ drill-down แบบอ่านอย่างเดียว; แก้ technical finding ไม่ได้",
        [
            PermissionCatalog.AssetRead, PermissionCatalog.InstanceRead,
            PermissionCatalog.DashboardExec,
        ]),
    ];

    private const string OrgScopeValue = "*";

    private static readonly ScopeSeed OrganizationScope = new(ScopeType.Organization, OrgScopeValue);

    private static ScopeSeed BuScope(string businessUnit) => new(ScopeType.BusinessUnit, businessUnit);

    /// <summary>
    /// demo users (ข้อมูลสมมติ, อีเมล @svm.example) อย่างน้อย 1 คนต่อ role · FR-01..03, FR-63.
    /// ไม่มีรหัสผ่านในโค้ด — สร้างเมื่อ <c>Seed:DemoPassword</c> ไม่ว่างเท่านั้น.
    /// scope: admin/cyberadmin/officer/analyst/approver/auditor/exec = <c>Organization:*</c> (ให้ชัดเจน);
    /// assetowner/remediation = <c>BusinessUnit</c> ตาม BU ของ asset ตัวอย่าง.
    /// </summary>
    internal static IReadOnlyList<DemoUserSeed> DemoUsers { get; } =
    [
        new("admin", "Demo System Admin", "admin@svm.example", SeedRoleCodes.SystemAdmin, [OrganizationScope]),
        new("cyberadmin1", "Demo Cyber Admin", "cyberadmin1@svm.example", SeedRoleCodes.CyberAdmin, [OrganizationScope]),
        new("officer1", "Demo VM Officer", "officer1@svm.example", SeedRoleCodes.VulnMgmtOfficer, [OrganizationScope]),
        new("analyst1", "Demo Security Analyst", "analyst1@svm.example", SeedRoleCodes.SecurityAnalyst, [OrganizationScope]),
        new("assetowner1", "Demo Asset Owner 1", "assetowner1@svm.example", SeedRoleCodes.AssetOwner,
            [BuScope(SeedBusinessUnits.DigitalChannels), BuScope(SeedBusinessUnits.ItInfrastructure)]),
        new("assetowner2", "Demo Asset Owner 2", "assetowner2@svm.example", SeedRoleCodes.AssetOwner,
            [BuScope(SeedBusinessUnits.CorporateFinance), BuScope(SeedBusinessUnits.HumanResources)]),
        new("remediation1", "Demo Remediation Owner 1", "remediation1@svm.example", SeedRoleCodes.RemediationOwner,
            [BuScope(SeedBusinessUnits.ItInfrastructure), BuScope(SeedBusinessUnits.CorporateFinance), BuScope(SeedBusinessUnits.HumanResources)]),
        new("remediation2", "Demo Remediation Owner 2", "remediation2@svm.example", SeedRoleCodes.RemediationOwner,
            [BuScope(SeedBusinessUnits.DigitalChannels)]),
        new("approver1", "Demo Risk Approver", "approver1@svm.example", SeedRoleCodes.RiskApprover, [OrganizationScope]),
        new("auditor1", "Demo Auditor", "auditor1@svm.example", SeedRoleCodes.Auditor, [OrganizationScope]),
        new("exec1", "Demo Executive", "exec1@svm.example", SeedRoleCodes.Executive, [OrganizationScope]),
    ];
}
