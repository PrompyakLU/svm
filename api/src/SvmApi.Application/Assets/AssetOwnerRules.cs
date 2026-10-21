using SvmApi.Domain.Assets;
using SvmApi.Domain.Remediation;

namespace SvmApi.Application.Assets;

/// <summary>ผลการ match assignment rule สำหรับ asset ใหม่ (FR-03): ทีมแก้ไขที่จะผูกเป็น RemediationTeam.</summary>
public sealed record RuleOwnerMatch(Guid RuleId, string TeamName, Guid? UserId);

/// <summary>
/// FR-03 (US-006): "WHEN asset ใหม่เข้าระบบ, THEN เชื่อม owner จาก source หรือ assignment rule".
/// pure logic — ใช้ <c>config_assignmentRule</c> เป็น fallback เฉพาะ RemediationTeam เมื่อ source/ผู้สร้างไม่ได้ระบุทีม.
/// นโยบาย:
/// <list type="bullet">
/// <item>พิจารณาเฉพาะ rule ที่ active และมี <c>AssignToTeam</c> (ownership แบบ RemediationTeam ต้องมีชื่อทีม)</item>
/// <item>match type ที่ใช้ได้กับ asset: <c>BusinessUnit</c> (= asset.businessUnit) และ <c>AssetType</c> (= asset.assetType);
/// <c>Technology</c> ไม่มี field ใน master_asset จึงไม่ match</item>
/// <item>เทียบค่าแบบไม่สนตัวพิมพ์ + trim; เรียงตาม priority (น้อย = ก่อน) แล้ว Id เพื่อให้ผลคงที่; ใช้ rule แรกที่ match</item>
/// </list>
/// </summary>
public static class AssetOwnerRules
{
    /// <summary>true ถ้ารายการ owner มี RemediationTeam อยู่แล้ว (ไม่ต้องใช้ rule).</summary>
    public static bool HasRemediationTeam(IEnumerable<OwnerType> ownerTypes)
    {
        ArgumentNullException.ThrowIfNull(ownerTypes);
        return ownerTypes.Any(t => t == OwnerType.RemediationTeam);
    }

    public static RuleOwnerMatch? Match(IEnumerable<AssignmentRule> rules, string? businessUnit, AssetType assetType)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var bu = AssetRules.Clean(businessUnit);
        var type = assetType.ToString();

        foreach (var rule in rules
                     .Where(r => r is not null && r.IsActive)
                     .OrderBy(r => r.Priority)
                     .ThenBy(r => r.Id))
        {
            var team = AssetRules.Clean(rule.AssignToTeam);
            var value = AssetRules.Clean(rule.MatchValue);
            if (team is null || value is null || team.Length > AssetRules.MaxTeamNameLength)
            {
                continue;
            }

            var matched = rule.MatchType switch
            {
                AssignmentMatchType.BusinessUnit => bu is not null && string.Equals(value, bu, StringComparison.OrdinalIgnoreCase),
                AssignmentMatchType.AssetType => string.Equals(value, type, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

            if (matched)
            {
                var userId = rule.AssignToUserId == Guid.Empty ? null : rule.AssignToUserId;
                return new RuleOwnerMatch(rule.Id, team, userId);
            }
        }

        return null;
    }
}
