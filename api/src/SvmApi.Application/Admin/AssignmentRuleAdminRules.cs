using SvmApi.Domain.Assets;
using SvmApi.Domain.Remediation;

namespace SvmApi.Application.Admin;

/// <summary>กฎมอบหมายที่ parse แล้ว (ยังไม่ตรวจว่า assignToUserId มีอยู่จริง — ผู้เรียกตรวจกับ DB).</summary>
public sealed record AssignmentRuleDraft(
    AssignmentMatchType MatchType,
    string MatchValue,
    Guid? AssignToUserId,
    string? AssignToTeam,
    int Priority,
    bool IsActive);

/// <summary>แถวกฎสำหรับตรวจชุด (ไม่ผูก EF).</summary>
public sealed record AssignmentRuleRow(
    Guid Id,
    AssignmentMatchType MatchType,
    string MatchValue,
    Guid? AssignToUserId,
    string? AssignToTeam,
    int Priority,
    bool IsActive);

/// <summary>
/// กติกาแก้ไข <c>config_assignmentRule</c> ฝั่ง admin (FR-24 · BR-07 · EX-59) — pure ไม่แตะ DB.
/// <list type="bullet">
/// <item>matchType เป็นค่าที่นิยามไว้; matchValue ไม่ว่าง ≤ 200 อักขระ; AssetType ต้องเป็นชื่อ <see cref="AssetType"/> (เก็บเป็นชื่อมาตรฐาน);
///   Technology ≥ 2 อักขระ (ตรงกับที่ <c>AssignmentLogic</c> ใช้จับคู่).</item>
/// <item>ต้องมีผู้รับอย่างน้อยหนึ่งอย่าง: assignToUserId หรือ assignToTeam (กฎไม่มีผู้รับถูก <c>AssignmentLogic</c> ข้าม = EX-59).</item>
/// <item>priority ∈ [1, <see cref="MaxPriority"/>] (น้อยก่อน). ลำดับประเมินแน่นอนเสมอ: priority → ความเฉพาะเจาะจง → matchValue → team → user (<see cref="Order"/>);
///   priority ซ้ำกันได้เพราะมี tie-break แน่นอน แต่ <b>คู่ (matchType, matchValue) ซ้ำกันไม่ได้</b> ในหมู่กฎ active.</item>
/// </list>
/// </summary>
public static class AssignmentRuleAdminRules
{
    public const int MaxValueLength = 200;
    public const int MinTechnologyLength = 2;
    public const int MinPriority = 1;
    public const int MaxPriority = 10_000;

    public static (AssignmentRuleDraft? Value, AdminValidation Validation) Parse(AssignmentRuleRequest? request, bool isUpdate)
    {
        var issues = new List<AdminIssue>();
        if (request is null)
        {
            issues.Add(new AdminIssue("body", "REQUIRED"));
            return (null, AdminValidation.From(issues));
        }

        var typeOk = AdminParse.TryEnum<AssignmentMatchType>(request.MatchType, out var matchType);
        if (!typeOk)
        {
            issues.Add(new AdminIssue("matchType", "INVALID_MATCH_TYPE"));
        }

        var value = AdminParse.CleanText(request.MatchValue);
        if (value is null)
        {
            issues.Add(new AdminIssue("matchValue", "REQUIRED"));
        }
        else if (value.Length > MaxValueLength)
        {
            issues.Add(new AdminIssue("matchValue", "TOO_LONG"));
        }
        else if (typeOk)
        {
            switch (matchType)
            {
                case AssignmentMatchType.AssetType:
                    if (AdminParse.TryEnum<AssetType>(value, out var assetType))
                    {
                        value = assetType.ToString();
                    }
                    else
                    {
                        issues.Add(new AdminIssue("matchValue", "INVALID_ASSET_TYPE"));
                    }

                    break;

                case AssignmentMatchType.Technology when value.Length < MinTechnologyLength:
                    issues.Add(new AdminIssue("matchValue", "TECHNOLOGY_TOO_SHORT"));
                    break;
            }
        }

        var team = AdminParse.CleanText(request.AssignToTeam);
        if (team is { Length: > MaxValueLength })
        {
            issues.Add(new AdminIssue("assignToTeam", "TOO_LONG"));
        }

        if (request.AssignToUserId == Guid.Empty)
        {
            issues.Add(new AdminIssue("assignToUserId", "INVALID_ID"));
        }
        else if (request.AssignToUserId is null && team is null)
        {
            issues.Add(new AdminIssue("assignToTeam", "ASSIGNEE_REQUIRED", IsBusinessRule: true));
        }

        if (request.Priority is null)
        {
            issues.Add(new AdminIssue("priority", "REQUIRED"));
        }
        else if (request.Priority.Value is < MinPriority or > MaxPriority)
        {
            issues.Add(new AdminIssue("priority", $"PRIORITY_OUT_OF_RANGE: {MinPriority}..{MaxPriority}", IsBusinessRule: true));
        }

        if (isUpdate && request.IsActive is null)
        {
            issues.Add(new AdminIssue("isActive", "REQUIRED"));
        }

        if (issues.Count > 0)
        {
            return (null, AdminValidation.From(issues));
        }

        return (
            new AssignmentRuleDraft(matchType, value!, request.AssignToUserId, team, request.Priority!.Value, request.IsActive ?? true),
            AdminValidation.Valid);
    }

    /// <summary>คีย์ซ้ำของกฎ: <c>BusinessUnit|IT INFRA</c> (ไม่สนตัวพิมพ์ — ตรงกับการจับคู่ของ engine).</summary>
    public static string DuplicateKey(AssignmentRuleRow rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return $"{rule.MatchType}|{rule.MatchValue.Trim().ToUpperInvariant()}";
    }

    /// <summary>ตรวจชุดกฎหลังแก้: ไม่มี (matchType, matchValue) ซ้ำในหมู่กฎ active.</summary>
    public static AdminValidation ValidateSet(IReadOnlyCollection<AssignmentRuleRow> after)
    {
        ArgumentNullException.ThrowIfNull(after);

        var issues = after
            .Where(r => r.IsActive)
            .GroupBy(DuplicateKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new AdminIssue("matchValue", $"DUPLICATE_RULE: {g.Key}", IsBusinessRule: true));

        return AdminValidation.From(issues);
    }

    /// <summary>ความเฉพาะเจาะจง (น้อย = เฉพาะเจาะจงกว่า) ตามที่ <c>AssignmentLogic</c> ใช้: Technology &lt; AssetType &lt; BusinessUnit.</summary>
    public static int Specificity(AssignmentMatchType type) => type switch
    {
        AssignmentMatchType.Technology => 0,
        AssignmentMatchType.AssetType => 1,
        _ => 2,
    };

    /// <summary>เรียงกฎตามลำดับประเมินที่แน่นอน (deterministic) — ลำดับเดียวกับ <c>AssignmentLogic</c> บวก Id เป็นตัวตัดสินสุดท้าย.</summary>
    public static IReadOnlyList<AssignmentRuleRow> Order(IEnumerable<AssignmentRuleRow> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules
            .OrderBy(r => r.Priority)
            .ThenBy(r => Specificity(r.MatchType))
            .ThenBy(r => r.MatchValue.Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AssignToTeam?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AssignToUserId ?? Guid.Empty)
            .ThenBy(r => r.Id)
            .ToList();
    }
}
