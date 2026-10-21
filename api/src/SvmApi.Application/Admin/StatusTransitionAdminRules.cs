using SvmApi.Application.Workflow;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Admin;

/// <summary>ค่าที่ผ่าน parse ของ transition หนึ่งแถว (ยังไม่ผ่านการตรวจระดับตาราง).</summary>
public sealed record ParsedTransition(
    VulnerabilityStatus From,
    VulnerabilityStatus To,
    string? RequiredPermission,
    bool RequiresEvidence,
    bool RequiresApproval,
    bool IsActive);

/// <summary>
/// กติกาแก้ไข <c>config_statusTransition</c> ฝั่ง admin (FR-23/65 · BR-13 · BR-27 · BR-40) — pure ไม่แตะ DB.
/// <para>
/// แนวคิด: ตรวจ "ตารางหลังแก้" (<see cref="ValidateTable"/>) แล้วรายงานเฉพาะปัญหาที่ <b>เกิดใหม่</b> เทียบกับตารางก่อนแก้ (<see cref="ValidateChange"/>)
/// เพื่อไม่ให้ฐานข้อมูลที่ไม่สมบูรณ์อยู่แล้วแก้ตัวเองไม่ได้ แต่ห้ามแก้ให้แย่ลง.
/// </para>
/// สิ่งที่ <see cref="WorkflowGuard"/> สมมติไว้และถูกป้องกันที่นี่:
/// <list type="bullet">
/// <item>BR-13: ต้องมี transition Resolved→Reopened และ Closed→Reopened ที่ active เสมอ (<see cref="WorkflowGuard.CanReopen"/> ใช้ระบบ reopen เมื่อพบซ้ำ/verify ไม่ผ่าน).</item>
/// <item>ไม่มีสถานะทางตัน: ทุกสถานะ (ยกเว้น <see cref="TerminalStatus"/>) ต้องมี transition ออกที่ active อย่างน้อยหนึ่งทาง.</item>
/// <item>ทุกสถานะต้องไปถึงได้จาก <see cref="InitialStatus"/> ผ่าน transition ที่ active.</item>
/// <item>(from,to) ไม่ซ้ำ (guard ค้นหาแถวเดียวต่อคู่), from≠to, สถานะต้องเป็นค่าที่นิยามไว้, requiredPermission ต้องเป็น permission code ที่มีอยู่จริง.</item>
/// </list>
/// </summary>
public static class StatusTransitionAdminRules
{
    public const int MaxPermissionLength = 100;

    /// <summary>สถานะเริ่มต้นของ instance ใหม่.</summary>
    public const VulnerabilityStatus InitialStatus = VulnerabilityStatus.New;

    /// <summary>สถานะปลายทางที่ไม่ต้องมี transition ออก.</summary>
    public const VulnerabilityStatus TerminalStatus = VulnerabilityStatus.Archived;

    /// <summary>transition ที่ระบบพึ่งพา (BR-13) — ห้ามหายไปหรือถูกปิด.</summary>
    public static readonly IReadOnlyList<(VulnerabilityStatus From, VulnerabilityStatus To)> ProtectedTransitions =
    [
        (VulnerabilityStatus.Resolved, VulnerabilityStatus.Reopened),
        (VulnerabilityStatus.Closed, VulnerabilityStatus.Reopened),
    ];

    // ------------------------------------------------------------------ parse

    public static (ParsedTransition? Value, AdminValidation Validation) ParseCreate(CreateStatusTransitionRequest? request)
    {
        var issues = new List<AdminIssue>();
        if (request is null)
        {
            issues.Add(new AdminIssue("body", "REQUIRED"));
            return (null, AdminValidation.From(issues));
        }

        var fromOk = AdminParse.TryEnum<VulnerabilityStatus>(request.FromStatus, out var from);
        if (!fromOk)
        {
            issues.Add(new AdminIssue("fromStatus", "INVALID_STATUS"));
        }

        var toOk = AdminParse.TryEnum<VulnerabilityStatus>(request.ToStatus, out var to);
        if (!toOk)
        {
            issues.Add(new AdminIssue("toStatus", "INVALID_STATUS"));
        }

        var permission = ParsePermission(request.RequiredPermission, issues);
        if (issues.Count > 0)
        {
            return (null, AdminValidation.From(issues));
        }

        return (
            new ParsedTransition(
                from, to, permission, request.RequiresEvidence ?? false, request.RequiresApproval ?? false, request.IsActive ?? true),
            AdminValidation.Valid);
    }

    /// <summary>แปลงคำขอแก้ไข (แทนค่าทั้งก้อน) — from/to ไม่อยู่ในคำขอ (แก้ไม่ได้); ต้องส่ง flag ครบ.</summary>
    public static (UpdateStatusTransitionRequest? Value, AdminValidation Validation) ParseUpdate(
        UpdateStatusTransitionRequest? request, out string? normalizedPermission)
    {
        normalizedPermission = null;
        var issues = new List<AdminIssue>();
        if (request is null)
        {
            issues.Add(new AdminIssue("body", "REQUIRED"));
            return (null, AdminValidation.From(issues));
        }

        if (request.RequiresEvidence is null)
        {
            issues.Add(new AdminIssue("requiresEvidence", "REQUIRED"));
        }

        if (request.RequiresApproval is null)
        {
            issues.Add(new AdminIssue("requiresApproval", "REQUIRED"));
        }

        if (request.IsActive is null)
        {
            issues.Add(new AdminIssue("isActive", "REQUIRED"));
        }

        normalizedPermission = ParsePermission(request.RequiredPermission, issues);
        return issues.Count > 0 ? (null, AdminValidation.From(issues)) : (request, AdminValidation.Valid);
    }

    private static string? ParsePermission(string? raw, List<AdminIssue> issues)
    {
        var permission = AdminParse.CleanText(raw);
        if (permission is not null && (permission.Length > MaxPermissionLength || permission.Any(char.IsWhiteSpace)))
        {
            issues.Add(new AdminIssue("requiredPermission", "INVALID_PERMISSION_CODE"));
            return null;
        }

        return permission;
    }

    // ------------------------------------------------------------------ table rules

    /// <summary>
    /// ตรวจทั้งตาราง. รหัสที่คืน (ใช้เทียบก่อน/หลังแก้): <c>UNKNOWN_STATUS</c>, <c>SELF_TRANSITION</c>, <c>DUPLICATE_TRANSITION: A-&gt;B</c>,
    /// <c>UNKNOWN_PERMISSION: code</c>, <c>PROTECTED_TRANSITION_REQUIRED: A-&gt;B</c> (BR-13), <c>DEAD_END: S</c>, <c>UNREACHABLE: S</c>.
    /// </summary>
    public static IReadOnlyList<string> ValidateTable(
        IReadOnlyCollection<TransitionRule> rules,
        IReadOnlySet<string> knownPermissions)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(knownPermissions);

        var issues = new List<string>();

        var seen = new HashSet<(VulnerabilityStatus, VulnerabilityStatus)>();
        foreach (var rule in rules)
        {
            if (!Enum.IsDefined(rule.From) || !Enum.IsDefined(rule.To))
            {
                issues.Add("UNKNOWN_STATUS");
                continue;
            }

            if (rule.From == rule.To)
            {
                issues.Add($"SELF_TRANSITION: {rule.From}");
            }

            if (!seen.Add((rule.From, rule.To)))
            {
                issues.Add($"DUPLICATE_TRANSITION: {rule.From}->{rule.To}");
            }

            // requiredPermission ว่าง = WorkflowGuard ใช้ instance.transition (fail-closed) จึงไม่ตรวจ
            if (!string.IsNullOrWhiteSpace(rule.RequiredPermission)
                && !knownPermissions.Contains(rule.RequiredPermission.Trim()))
            {
                issues.Add($"UNKNOWN_PERMISSION: {rule.RequiredPermission.Trim()}");
            }
        }

        var active = rules
            .Where(r => r.IsActive && Enum.IsDefined(r.From) && Enum.IsDefined(r.To) && r.From != r.To)
            .ToList();

        // BR-13
        foreach (var (from, to) in ProtectedTransitions)
        {
            if (!active.Any(r => r.From == from && r.To == to))
            {
                issues.Add($"PROTECTED_TRANSITION_REQUIRED: {from}->{to}");
            }
        }

        // ไม่มีทางตัน
        foreach (var status in Enum.GetValues<VulnerabilityStatus>())
        {
            if (status != TerminalStatus && !active.Any(r => r.From == status))
            {
                issues.Add($"DEAD_END: {status}");
            }
        }

        // ถึงได้จากสถานะเริ่มต้น (BFS บน transition ที่ active)
        var reachable = new HashSet<VulnerabilityStatus> { InitialStatus };
        var queue = new Queue<VulnerabilityStatus>();
        queue.Enqueue(InitialStatus);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in active.Where(r => r.From == current).Select(r => r.To))
            {
                if (reachable.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        foreach (var status in Enum.GetValues<VulnerabilityStatus>().Where(s => !reachable.Contains(s)))
        {
            issues.Add($"UNREACHABLE: {status}");
        }

        return issues;
    }

    /// <summary>
    /// ตรวจการเปลี่ยนแปลง: คืนเฉพาะปัญหาที่ตาราง <paramref name="after"/> มีแต่ <paramref name="before"/> ไม่มี (ไม่ทำให้แย่ลง).
    /// รหัสถูกรายงานเป็น business rule (→ 422 <c>INVALID_STATUS_TRANSITION</c>).
    /// </summary>
    public static AdminValidation ValidateChange(
        IReadOnlyCollection<TransitionRule> before,
        IReadOnlyCollection<TransitionRule> after,
        IReadOnlySet<string> knownPermissions)
    {
        var existing = ValidateTable(before, knownPermissions).ToHashSet(StringComparer.Ordinal);
        var introduced = ValidateTable(after, knownPermissions)
            .Where(code => !existing.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .Select(code => new AdminIssue("transitions", code, IsBusinessRule: true));

        return AdminValidation.From(introduced);
    }
}
