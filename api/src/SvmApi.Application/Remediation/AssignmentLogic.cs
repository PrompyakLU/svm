using SvmApi.Application.Common;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Remediation;

/// <summary>กฎมอบหมายที่ดึงจาก <c>config_assignmentRule</c> (pure).</summary>
public sealed record AssignmentRuleFacts(
    AssignmentMatchType MatchType,
    string MatchValue,
    Guid? AssignToUserId,
    string? AssignToTeam,
    int Priority,
    bool IsActive);

/// <summary>คุณสมบัติของ instance/asset ที่ใช้จับคู่กฎ.</summary>
public sealed record AssetMatchFacts(string? BusinessUnit, string? AssetType, string? Technology);

/// <summary>สิทธิ์ขอบเขตข้อมูลของผู้ใช้ (ชนิดเดียวกับ <c>config_userScope</c>): Organization/BusinessUnit/Asset/Responsibility.</summary>
public sealed record ScopeGrantFacts(string Type, string Value);

/// <summary>เลือกกฎมอบหมายอัตโนมัติ (FR-24 · BR-07 · EX-59). Pure — ไม่แตะ DB.</summary>
public static class AssignmentRuleMatcher
{
    /// <summary>
    /// คืนกฎแรกที่ตรงตามลำดับ: Priority น้อยก่อน → ความเฉพาะเจาะจง (Technology &lt; AssetType &lt; BusinessUnit) → MatchValue (ordinal, ไม่สนตัวพิมพ์)
    /// เพื่อให้ผลคงที่ (deterministic). ข้ามกฎที่ปิดอยู่ ค่าว่าง หรือไม่มี assignee (กฎตั้งค่าผิด — EX-59).
    /// Technology จับคู่แบบ "ชื่อ component มีค่าที่กำหนด" (ขั้นต่ำ 2 ตัวอักษร); ชนิดอื่นจับคู่ตรงตัว.
    /// </summary>
    public static AssignmentRuleFacts? Match(IEnumerable<AssignmentRuleFacts> rules, AssetMatchFacts facts)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(facts);

        return rules
            .Where(r => r.IsActive && HasTarget(r) && !string.IsNullOrWhiteSpace(r.MatchValue) && Matches(r, facts))
            .OrderBy(r => r.Priority)
            .ThenBy(r => Specificity(r.MatchType))
            .ThenBy(r => r.MatchValue.Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AssignToTeam?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AssignToUserId ?? Guid.Empty)
            .FirstOrDefault();
    }

    private static bool HasTarget(AssignmentRuleFacts r)
        => (r.AssignToUserId is { } id && id != Guid.Empty) || !string.IsNullOrWhiteSpace(r.AssignToTeam);

    private static int Specificity(AssignmentMatchType type) => type switch
    {
        AssignmentMatchType.Technology => 0,
        AssignmentMatchType.AssetType => 1,
        _ => 2,
    };

    private static bool Matches(AssignmentRuleFacts rule, AssetMatchFacts facts)
    {
        var value = rule.MatchValue.Trim();
        return rule.MatchType switch
        {
            AssignmentMatchType.BusinessUnit => Eq(facts.BusinessUnit, value),
            AssignmentMatchType.AssetType => Eq(facts.AssetType, value),
            AssignmentMatchType.Technology => value.Length >= 2
                && facts.Technology is { Length: > 0 } tech
                && tech.Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool Eq(string? actual, string expected)
        => actual is not null && string.Equals(actual.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// ตรวจว่าผู้ใช้ (assignee / ผู้ถูก mention) มีขอบเขตข้อมูลครอบคลุม asset หรือไม่ — กติกาเดียวกับ <c>ApplyDataScope</c> (fail-closed).
/// Organization ต้องเป็น <c>*</c> เท่านั้น; Responsibility = เป็น owner ของ asset.
/// </summary>
public static class AssigneeScopeMatcher
{
    public static bool HasScope(IEnumerable<ScopeGrantFacts> grants, string? assetBusinessUnit, string? assetCode, bool isAssetOwner)
    {
        ArgumentNullException.ThrowIfNull(grants);

        foreach (var grant in grants)
        {
            var value = grant.Value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (Is(grant.Type, "Organization"))
            {
                if (value == "*")
                {
                    return true;
                }
            }
            else if (Is(grant.Type, "BusinessUnit"))
            {
                if (assetBusinessUnit is not null && string.Equals(assetBusinessUnit.Trim(), value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (Is(grant.Type, "Asset"))
            {
                if (assetCode is not null && string.Equals(assetCode.Trim(), value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (Is(grant.Type, "Responsibility"))
            {
                if (isAssetOwner)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Is(string? actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>กติกา pure ของการมอบหมาย: สถานะที่มอบหมายได้, ผู้รับ, เหตุผล, due date เริ่มต้นของ task.</summary>
public static class AssignmentPolicy
{
    public const int MaxReasonLength = 1000;
    public const int MaxTeamLength = 200;
    public const int MaxBulkSize = 200;

    private static readonly HashSet<VulnerabilityStatus> Blocked =
    [
        VulnerabilityStatus.PendingVerification,
        VulnerabilityStatus.Resolved,
        VulnerabilityStatus.Closed,
        VulnerabilityStatus.FalsePositive,
        VulnerabilityStatus.RiskAccepted,
        VulnerabilityStatus.ExceptionApproved,
        VulnerabilityStatus.Archived,
    ];

    /// <summary>มอบหมายไม่ได้เมื่อ instance อยู่ในสถานะท้าย/รอ verify/ยกเว้น.</summary>
    public static bool CanAssignIn(VulnerabilityStatus status) => !Blocked.Contains(status);

    /// <summary>ผู้รับที่ระบุตรง ๆ: ต้องมีอย่างใดอย่างหนึ่ง (user หรือ team) ไม่ใช่ทั้งคู่. คืน error code หรือ null.</summary>
    public static string? ValidateExplicitTarget(Guid? userId, string? team)
    {
        var hasUser = userId is { } id && id != Guid.Empty;
        var hasTeam = !string.IsNullOrWhiteSpace(team);

        if (hasUser && hasTeam)
        {
            return RemediationErrorCodes.AssigneeConflict;
        }

        if (hasTeam && NormalizeTeam(team)!.Length > MaxTeamLength)
        {
            return RemediationErrorCodes.TeamTooLong;
        }

        return null;
    }

    /// <summary>true เมื่อไม่ได้ระบุผู้รับ → ใช้กฎอัตโนมัติ (FR-24).</summary>
    public static bool IsAutomatic(Guid? userId, string? team)
        => (userId is null || userId == Guid.Empty) && string.IsNullOrWhiteSpace(team);

    public static string? NormalizeTeam(string? team)
    {
        var cleaned = TextSanitizer.Clean(team, allowLineBreaks: false);
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>เหตุผลบังคับเมื่อเปลี่ยนผู้รับผิดชอบเดิมเป็นคนอื่น (FR-25) — ไม่บังคับกับการมอบหมายครั้งแรกหรือกฎอัตโนมัติ.</summary>
    public static bool ReasonRequired(bool automatic, bool hadPreviousAssignee, bool targetChanged)
        => !automatic && hadPreviousAssignee && targetChanged;

    public static string? NormalizeReason(string? reason)
    {
        var cleaned = TextSanitizer.Clean(reason, allowLineBreaks: true);
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>
    /// วันครบกำหนดของ task: ใช้ SLA resolution due ถ้ามี, ไม่งั้นค่าเริ่มต้นตามระดับความเสี่ยง
    /// (Critical 7 / High 30 / Medium 90 / อื่น ๆ 180 วัน) จนกว่า SlaModule (5.3) จะสร้าง timer.
    /// </summary>
    public static DateTime DefaultDueDate(DateTime nowUtc, RiskLevel? level, DateTime? slaResolutionDue)
    {
        if (slaResolutionDue is { } due)
        {
            return due;
        }

        var days = level switch
        {
            RiskLevel.Critical => 7,
            RiskLevel.High => 30,
            RiskLevel.Medium => 90,
            _ => 180,
        };

        return nowUtc.AddDays(days);
    }
}
