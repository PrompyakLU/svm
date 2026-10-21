using SvmApi.Application.Risk;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Admin;

/// <summary>
/// กติกาแก้ไข risk weight/threshold ฝั่ง admin (EX-54 · BR-40) — pure ไม่แตะ DB.
/// ใช้ <see cref="RiskScoringEngine.ValidateWeights"/> / <see cref="RiskScoringEngine.ValidateThresholds"/> ตัวเดียวกับ engine
/// (admin และ engine ใช้กติกาเดียวกัน) แล้วเพิ่มเฉพาะกติกาของการ "แทนที่ทั้งชุด": ต้องครบทุก factor/level และความละเอียดทศนิยม
/// ไม่เกินที่คอลัมน์เก็บได้ (decimal(5,4) / decimal(5,2)) เพื่อไม่ให้ถูกปัดเงียบ ๆ.
/// </summary>
public static class RiskConfigAdminRules
{
    public const int MaxWeightDecimals = 4;
    public const int MaxScoreDecimals = 2;
    public const int MaxItems = 50;

    /// <summary>
    /// แปลง + ตรวจชุด weight. คืน <c>Value</c> เมื่อรูปแบบถูกต้อง (แม้ผิดกติกาธุรกิจ เพื่อให้ dry-run รายงานผลรวมได้);
    /// <c>Value</c> เป็น null เมื่อรูปแบบข้อมูลผิด.
    /// </summary>
    public static (IReadOnlyList<RiskWeightConfig>? Value, AdminValidation Validation) ParseWeights(ReplaceRiskWeightsRequest? request)
    {
        var issues = new List<AdminIssue>();
        var items = request?.Weights;
        if (items is null)
        {
            issues.Add(new AdminIssue("weights", "REQUIRED"));
            return (null, AdminValidation.From(issues));
        }

        if (items.Count > MaxItems)
        {
            issues.Add(new AdminIssue("weights", "TOO_MANY"));
            return (null, AdminValidation.From(issues));
        }

        var parsed = new List<RiskWeightConfig>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                issues.Add(new AdminIssue($"weights[{i}]", "REQUIRED"));
                continue;
            }

            var factorOk = AdminParse.TryEnum<RiskFactor>(item.Factor, out var factor);
            if (!factorOk)
            {
                issues.Add(new AdminIssue($"weights[{i}].factor", "INVALID_FACTOR"));
            }

            if (item.Weight is null)
            {
                issues.Add(new AdminIssue($"weights[{i}].weight", "REQUIRED"));
            }

            if (factorOk && item.Weight is { } weight)
            {
                // ปัดเงียบ ๆ ไม่ได้: 0.12345 จะถูกเก็บเป็น 0.1235 ทำให้ผลรวมที่ตรวจไม่ตรงกับที่เก็บ
                if (AdminParse.DecimalPlaces(weight) > MaxWeightDecimals)
                {
                    issues.Add(new AdminIssue($"weights[{i}].weight", "WEIGHT_PRECISION", IsBusinessRule: true));
                }

                parsed.Add(new RiskWeightConfig(factor, weight, item.IsActive ?? true));
            }
        }

        if (issues.Any(x => !x.IsBusinessRule))
        {
            return (null, AdminValidation.From(issues));
        }

        issues.AddRange(CheckWeights(parsed));
        return (parsed, AdminValidation.From(issues));
    }

    /// <summary>กติกาของชุด weight ที่แปลงแล้ว: กติกา engine (EX-54) + ต้องครบทุก factor.</summary>
    public static IReadOnlyList<AdminIssue> CheckWeights(IReadOnlyCollection<RiskWeightConfig> weights)
    {
        var issues = new List<AdminIssue>();

        // กติกาเดียวกับ engine: ไม่ติดลบ/ไม่เกิน 1/ไม่ซ้ำ/ผลรวม active = 1.0000 ± tolerance
        try
        {
            var engine = RiskScoringEngine.ValidateWeights(weights);
            issues.AddRange(engine.Issues.Select(code => new AdminIssue("weights", code, IsBusinessRule: true)));
        }
        catch (OverflowException)
        {
            // Arbitrary JSON decimals must produce validation, never an arithmetic 500.
            issues.Add(new AdminIssue("weights", "WEIGHT_SUM_OVERFLOW", IsBusinessRule: true));
        }

        var present = weights.Select(w => w.Factor).ToHashSet();
        foreach (var factor in Enum.GetValues<RiskFactor>().Where(f => !present.Contains(f)))
        {
            issues.Add(new AdminIssue("weights", $"MISSING_FACTOR: {factor}", IsBusinessRule: true));
        }

        return issues;
    }

    /// <summary>ผลรวม weight ของแถว active.</summary>
    public static decimal ActiveSum(IEnumerable<RiskWeightConfig> weights) => weights.Where(w => w.IsActive).Sum(w => w.Weight);

    /// <summary>แปลง + ตรวจชุด threshold (ต้องครบทุก risk level และครอบ 0–100 ต่อเนื่อง ผ่าน <see cref="RiskScoringEngine.ValidateThresholds"/>).</summary>
    public static (IReadOnlyList<RiskThresholdConfig>? Value, AdminValidation Validation) ParseThresholds(ReplaceRiskThresholdsRequest? request)
    {
        var issues = new List<AdminIssue>();
        var items = request?.Thresholds;
        if (items is null)
        {
            issues.Add(new AdminIssue("thresholds", "REQUIRED"));
            return (null, AdminValidation.From(issues));
        }

        if (items.Count > MaxItems)
        {
            issues.Add(new AdminIssue("thresholds", "TOO_MANY"));
            return (null, AdminValidation.From(issues));
        }

        var parsed = new List<RiskThresholdConfig>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                issues.Add(new AdminIssue($"thresholds[{i}]", "REQUIRED"));
                continue;
            }

            var levelOk = AdminParse.TryEnum<RiskLevel>(item.RiskLevel, out var level);
            if (!levelOk)
            {
                issues.Add(new AdminIssue($"thresholds[{i}].riskLevel", "INVALID_RISK_LEVEL"));
            }

            if (item.MinScore is null)
            {
                issues.Add(new AdminIssue($"thresholds[{i}].minScore", "REQUIRED"));
            }

            if (item.MaxScore is null)
            {
                issues.Add(new AdminIssue($"thresholds[{i}].maxScore", "REQUIRED"));
            }

            if (levelOk && item.MinScore is { } min && item.MaxScore is { } max)
            {
                if (AdminParse.DecimalPlaces(min) > MaxScoreDecimals || AdminParse.DecimalPlaces(max) > MaxScoreDecimals)
                {
                    issues.Add(new AdminIssue($"thresholds[{i}]", "SCORE_PRECISION", IsBusinessRule: true));
                }

                parsed.Add(new RiskThresholdConfig(level, min, max));
            }
        }

        if (issues.Any(x => !x.IsBusinessRule))
        {
            return (null, AdminValidation.From(issues));
        }

        issues.AddRange(CheckThresholds(parsed));
        return (parsed, AdminValidation.From(issues));
    }

    /// <summary>กติกาของชุด threshold ที่แปลงแล้ว: กติกา engine (ต่อเนื่อง 0–100) + ต้องครบทุก risk level.</summary>
    public static IReadOnlyList<AdminIssue> CheckThresholds(IReadOnlyCollection<RiskThresholdConfig> thresholds)
    {
        var issues = new List<AdminIssue>();

        var engine = RiskScoringEngine.ValidateThresholds(thresholds);
        issues.AddRange(engine.Issues.Select(code => new AdminIssue("thresholds", code, IsBusinessRule: true)));

        var present = thresholds.Select(t => t.Level).ToHashSet();
        foreach (var level in Enum.GetValues<RiskLevel>().Where(l => !present.Contains(l)))
        {
            issues.Add(new AdminIssue("thresholds", $"MISSING_LEVEL: {level}", IsBusinessRule: true));
        }

        return issues;
    }
}
