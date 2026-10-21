namespace SvmApi.Infrastructure.Persistence.Seed;

/// <summary>นิยาม permission หนึ่งรายการ (code + คำอธิบายสั้น).</summary>
public sealed record PermissionDefinition(string Code, string Description);

/// <summary>
/// รายการ permission code ทั้งหมดของ SVM — รวบรวมจากคอลัมน์ Permission ใน api-spec.md
/// (<c>/api/v1</c>) · NFR-11/12 (RBAC, least privilege).
/// กติกา match: exact หรือ wildcard ระดับ family (<c>admin.*</c>) เท่านั้น — ไม่มี wildcard global
/// (ดู <c>PermissionMatcher</c>); catalog นี้ระบุ code แบบ explicit ทุกตัว.
/// </summary>
public static class PermissionCatalog
{
    // Assets (C2 · US-004..006)
    public const string AssetRead = "asset.read";
    public const string AssetWrite = "asset.write";
    public const string AssetCriticality = "asset.criticality";
    public const string AssetSync = "asset.sync";

    // Findings intake / dedup (C3, C4 · US-007..012)
    public const string FindingImport = "finding.import";
    public const string FindingCreate = "finding.create";
    public const string FindingDedup = "finding.dedup";

    // Reference data (C5 · US-013/014)
    public const string ReferenceRead = "reference.read";

    // Vulnerability instances / workflow (C6, C7 · US-015..019)
    public const string InstanceRead = "instance.read";
    public const string InstanceRiskAdjust = "instance.risk.adjust";
    public const string InstanceTransition = "instance.transition";
    public const string InstanceAssign = "instance.assign";

    /// <summary>เพิ่มเติมจาก api-spec: สิทธิ์อนุมัติ (False Positive / Risk Accepted / Exception) ใช้เป็น requiredPermission ของ transition ที่ requiresApproval.</summary>
    public const string InstanceApprove = "instance.approve";

    // Remediation (C8 · US-020..023)
    public const string TaskRead = "task.read";
    public const string TaskUpdate = "task.update";
    public const string EvidenceUpload = "evidence.upload";
    public const string CommentWrite = "comment.write";

    // Verification & closure (C10 · US-027..029)
    public const string VerificationSubmit = "verification.submit";
    public const string VerificationRescan = "verification.rescan";
    public const string VerificationRecord = "verification.record";

    // Dashboards (C11 · US-030)
    public const string DashboardOps = "dashboard.ops";
    public const string DashboardExec = "dashboard.exec";

    // Administration (C12 · US-031) — explicit codes, ไม่ใช้ wildcard
    public const string AdminMasterData = "admin.masterdata";
    public const string AdminWorkflow = "admin.workflow";
    public const string AdminSla = "admin.sla";
    public const string AdminRisk = "admin.risk";
    public const string AdminAssignment = "admin.assignment";
    public const string AdminUsers = "admin.users";

    // Audit (C13 · US-032)
    public const string AuditRead = "audit.read";

    /// <summary>เพิ่มเติมจาก api-spec: การ export audit ต้องได้รับอนุญาตและถูกบันทึก (personas.md — Auditor).</summary>
    public const string AuditExport = "audit.export";

    /// <summary>permission ทั้งหมดที่ seed ลง <c>config_permission</c>.</summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(AssetRead, "ดูรายการ/รายละเอียด asset (ภายใน data scope)"),
        new(AssetWrite, "สร้าง/แก้ไข asset"),
        new(AssetCriticality, "กำหนด asset criticality พร้อมเหตุผล"),
        new(AssetSync, "สั่ง sync asset จาก CMDB (mock)"),

        new(FindingImport, "นำเข้า finding จากไฟล์ และดู import batch"),
        new(FindingCreate, "ลงทะเบียน finding ด้วยมือ"),
        new(FindingDedup, "ตรวจ/ตัดสิน duplicate queue"),

        new(ReferenceRead, "ดู reference data (CVE/CWE/KEV)"),

        new(InstanceRead, "ดู vulnerability instance, history, SLA, evidence"),
        new(InstanceRiskAdjust, "ปรับ/คำนวณ risk score ใหม่ (ต้องมีเหตุผล)"),
        new(InstanceTransition, "เปลี่ยนสถานะ instance ผ่าน workflow"),
        new(InstanceAssign, "มอบหมาย instance (รวม bulk)"),
        new(InstanceApprove, "อนุมัติ False Positive / Risk Accepted / Exception (ห้ามอนุมัติรายการของตน)"),

        new(TaskRead, "ดู remediation task"),
        new(TaskUpdate, "อัปเดตแผน/ความคืบหน้า remediation task"),
        new(EvidenceUpload, "อัปโหลด evidence"),
        new(CommentWrite, "เพิ่ม comment และ mention"),

        new(VerificationSubmit, "ส่ง instance เข้า Pending Verification"),
        new(VerificationRescan, "ขอ rescan"),
        new(VerificationRecord, "บันทึกผล verification / ปิด-เปิดกลับ"),

        new(DashboardOps, "ดู operations dashboard"),
        new(DashboardExec, "ดู executive dashboard"),

        new(AdminMasterData, "จัดการ master data"),
        new(AdminWorkflow, "จัดการ status transition (workflow)"),
        new(AdminSla, "จัดการ SLA policy"),
        new(AdminRisk, "จัดการ risk weight/threshold"),
        new(AdminAssignment, "จัดการ assignment rule"),
        new(AdminUsers, "จัดการ user/role/permission"),

        new(AuditRead, "ค้นหา/ดู audit log (read-only)"),
        new(AuditExport, "export audit log (ถูกบันทึกเป็น audit event)"),
    ];
}
