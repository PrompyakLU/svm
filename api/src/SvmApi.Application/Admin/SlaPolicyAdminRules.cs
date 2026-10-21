using SvmApi.Application.Sla;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Admin;

/// <summary>
/// กติกาแก้ไข <c>config_slaPolicy</c> ฝั่ง admin (FR-66 · BR-08 · EX-66) — pure ไม่แตะ DB.
/// ใช้ <see cref="SlaCalculator.IsPolicyValid"/> / <see cref="SlaCalculator.MaxPolicyHours"/> ตัวเดียวกับ SLA engine.
/// <list type="bullet">
/// <item>ชั่วโมงทั้งสาม ∈ [1, <see cref="SlaCalculator.MaxPolicyHours"/>] และเรียง acknowledge ≤ start ≤ resolve.</item>
/// <item>scope key (riskLevel + criticality + internetExposure + kevOnly) ไม่ซ้ำในหมู่แถวที่ active (SelectPolicy ตัดสินเสมอกันไม่ได้อย่างมีความหมาย).</item>
/// <item>ทุก risk level ที่เคยมี policy active ต้องยังมีอย่างน้อยหนึ่งแถวที่ active (ไม่ให้เกิด EX-66 จากการแก้ config).</item>
/// </list>
/// </summary>
public static class SlaPolicyAdminRules
{
    /// <summary>แถว policy ที่ parse แล้ว (<see cref="SlaPolicyCandidate"/> ไม่ผูก EF).</summary>
    public static (SlaPolicyCandidate? Value, AdminValidation Validation) Parse(
        SlaPolicyRequest? request, Guid id, bool isUpdate)
    {
        var issues = new List<AdminIssue>();
        if (request is null)
        {
            issues.Add(new AdminIssue("body", "REQUIRED"));
            return (null, AdminValidation.From(issues));
        }

        var levelOk = AdminParse.TryEnum<RiskLevel>(request.RiskLevel, out var level);
        if (!levelOk)
        {
            issues.Add(new AdminIssue("riskLevel", "INVALID_RISK_LEVEL"));
        }

        AssetCriticality? criticality = null;
        if (!string.IsNullOrWhiteSpace(request.AssetCriticality))
        {
            if (AdminParse.TryEnum<AssetCriticality>(request.AssetCriticality, out var parsedCriticality))
            {
                criticality = parsedCriticality;
            }
            else
            {
                issues.Add(new AdminIssue("assetCriticality", "INVALID_CRITICALITY"));
            }
        }

        RequireHours(request.AcknowledgeHours, "acknowledgeHours", issues);
        RequireHours(request.StartHours, "startHours", issues);
        RequireHours(request.ResolveHours, "resolveHours", issues);

        if (isUpdate)
        {
            // PUT = แทนค่าทั้งก้อน: flag ต้องระบุชัดเจน (กันการล้างค่าโดยไม่ตั้งใจ)
            if (request.KevOnly is null)
            {
                issues.Add(new AdminIssue("kevOnly", "REQUIRED"));
            }

            if (request.IsActive is null)
            {
                issues.Add(new AdminIssue("isActive", "REQUIRED"));
            }
        }

        if (issues.Count > 0)
        {
            return (null, AdminValidation.From(issues));
        }

        var candidate = new SlaPolicyCandidate(
            id, level, criticality, request.InternetExposure, request.KevOnly ?? false,
            request.AcknowledgeHours!.Value, request.StartHours!.Value, request.ResolveHours!.Value,
            request.IsActive ?? true);

        return (candidate, AdminValidation.Valid);
    }

    private static void RequireHours(int? value, string field, List<AdminIssue> issues)
    {
        if (value is null)
        {
            issues.Add(new AdminIssue(field, "REQUIRED"));
        }
        else if (value.Value is < 1 or > SlaCalculator.MaxPolicyHours)
        {
            issues.Add(new AdminIssue(field, $"HOURS_OUT_OF_RANGE: 1..{SlaCalculator.MaxPolicyHours}", IsBusinessRule: true));
        }
    }

    /// <summary>ตรวจแถวเดียว (ช่วงชั่วโมงด้วยกติกาเดียวกับ engine + ลำดับ ack ≤ start ≤ resolve).</summary>
    public static IReadOnlyList<AdminIssue> ValidateRow(SlaPolicyCandidate policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var issues = new List<AdminIssue>();

        if (!SlaCalculator.IsPolicyValid(policy))
        {
            issues.Add(new AdminIssue("hours", SlaErrorCodes.PolicyInvalid, IsBusinessRule: true));
        }
        else if (policy.AcknowledgeHours > policy.StartHours || policy.StartHours > policy.ResolveHours)
        {
            issues.Add(new AdminIssue("hours", "HOURS_ORDER: acknowledge <= start <= resolve", IsBusinessRule: true));
        }

        if (!Enum.IsDefined(policy.RiskLevel))
        {
            issues.Add(new AdminIssue("riskLevel", "INVALID_RISK_LEVEL", IsBusinessRule: true));
        }

        return issues;
    }

    /// <summary>scope key ของ policy: <c>Critical|*|*|False</c> (criticality / internetExposure ที่ไม่ระบุ = <c>*</c>).</summary>
    public static string ScopeKey(SlaPolicyCandidate policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return string.Join(
            '|',
            policy.RiskLevel,
            policy.AssetCriticality?.ToString() ?? "*",
            policy.InternetExposure is { } exposure ? (exposure ? "True" : "False") : "*",
            policy.KevOnly ? "True" : "False");
    }

    /// <summary>
    /// ตรวจชุด policy หลังแก้: ไม่ซ้ำ scope key ในหมู่แถว active และไม่ทำให้ risk level ใดสูญเสียแถว active
    /// (เทียบกับ <paramref name="before"/>). <paramref name="changed"/> = แถวที่ถูกสร้าง/แก้ (ใช้ตรวจรายแถว).
    /// </summary>
    public static AdminValidation ValidateChange(
        IReadOnlyCollection<SlaPolicyCandidate> before,
        IReadOnlyCollection<SlaPolicyCandidate> after,
        SlaPolicyCandidate changed)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var issues = new List<AdminIssue>(ValidateRow(changed));

        var duplicate = after
            .Where(p => p.IsActive)
            .GroupBy(ScopeKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var key in duplicate)
        {
            issues.Add(new AdminIssue("scope", $"DUPLICATE_SCOPE: {key}", IsBusinessRule: true));
        }

        var activeBefore = before.Where(p => p.IsActive).Select(p => p.RiskLevel).ToHashSet();
        var activeAfter = after.Where(p => p.IsActive).Select(p => p.RiskLevel).ToHashSet();
        foreach (var level in activeBefore.Where(l => !activeAfter.Contains(l)).OrderBy(l => l))
        {
            issues.Add(new AdminIssue("riskLevel", $"NO_ACTIVE_POLICY: {level}", IsBusinessRule: true));
        }

        return AdminValidation.From(issues);
    }

    /// <summary>risk level ที่ได้รับผลกระทบจากการแก้ (ระดับเดิมและระดับใหม่ของแถว) ใช้กำหนดขอบเขต recalc timer.</summary>
    public static IReadOnlyCollection<RiskLevel> AffectedRiskLevels(SlaPolicyCandidate? previous, SlaPolicyCandidate current)
    {
        var levels = new HashSet<RiskLevel> { current.RiskLevel };
        if (previous is not null)
        {
            levels.Add(previous.RiskLevel);
        }

        return levels.OrderBy(l => l).ToList();
    }

    /// <summary>true เมื่อการแก้ทำให้ผลคำนวณ SLA เปลี่ยนได้ (ค่าที่ใช้คำนวณ/การเลือก policy ต่างจากเดิม).</summary>
    public static bool AffectsTimers(SlaPolicyCandidate? previous, SlaPolicyCandidate current)
        => previous is null
           ? current.IsActive
           : previous.IsActive != current.IsActive
             || previous.RiskLevel != current.RiskLevel
             || previous.AssetCriticality != current.AssetCriticality
             || previous.InternetExposure != current.InternetExposure
             || previous.KevOnly != current.KevOnly
             || previous.AcknowledgeHours != current.AcknowledgeHours
             || previous.StartHours != current.StartHours
             || previous.ResolveHours != current.ResolveHours;
}
