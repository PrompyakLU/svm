using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Risk;

/// <summary>
/// แกนคำนวณ risk แบบ weighted-sum (C6 · KD#1 · FR-16/17 · BR-01/02) — pure function ไม่มี DB/IO จึง unit test ได้.
/// <list type="number">
/// <item>ตรวจ weight (EX-54) และ threshold ก่อนเสมอ — config ผิด = ไม่คำนวณ.</item>
/// <item>normalize แต่ละปัจจัยเป็น [0,1] (clamp) → ปัจจัยที่มี weight &gt; 0 แต่ไม่มีข้อมูล = EX-50.</item>
/// <item>score = Σ(weight × normalized) × 100 → ตรวจช่วง (EX-51) → clamp [0,100] + ปัด 2 ตำแหน่ง (decimal(5,2)).</item>
/// <item>จัดระดับด้วย threshold: <c>min &lt;= score &lt; max</c> ยกเว้นช่วงบนสุดรวมค่าปลาย (<c>score &lt;= 100</c>).</item>
/// </list>
/// </summary>
public static class RiskScoringEngine
{
    public const string ModelName = "weighted-sum-v1";
    public const string ManualModelName = "manual-override";

    public const decimal MinScore = 0m;
    public const decimal MaxScore = 100m;

    /// <summary>ความคลาดเคลื่อนที่ยอมรับของผลรวม weight (weight เก็บเป็น decimal(5,4)).</summary>
    public const decimal WeightSumTolerance = 0.0001m;

    /// <summary>ความคลาดเคลื่อนของคะแนนก่อนถือว่าออกนอกช่วง (EX-51); ภายในนี้ถูก clamp.</summary>
    public const decimal ScoreTolerance = 0.01m;

    /// <summary>อายุช่องโหว่ (วัน) ที่ถือว่าเต็มสเกล.</summary>
    public const int VulnAgeFullScaleDays = 180;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ------------------------------------------------------------ validation

    /// <summary>
    /// ตรวจ weight (EX-54): ต้องมีอย่างน้อยหนึ่งแถว, factor ไม่ซ้ำและเป็นค่าที่รู้จัก, ทุกค่าอยู่ใน [0,1] (ไม่ติดลบ),
    /// และผลรวมของแถว active = 1.0000 (± <see cref="WeightSumTolerance"/>).
    /// </summary>
    public static RiskConfigValidation ValidateWeights(IReadOnlyCollection<RiskWeightConfig>? weights)
    {
        var issues = new List<string>();
        if (weights is null || weights.Count == 0)
        {
            issues.Add("NO_WEIGHTS");
            return new RiskConfigValidation(false, issues);
        }

        var seen = new HashSet<RiskFactor>();
        foreach (var w in weights)
        {
            if (!Enum.IsDefined(w.Factor))
            {
                issues.Add($"UNKNOWN_FACTOR: {(int)w.Factor}");
                continue;
            }

            if (!seen.Add(w.Factor))
            {
                issues.Add($"DUPLICATE_FACTOR: {w.Factor}");
            }

            if (w.Weight < 0m)
            {
                issues.Add($"NEGATIVE_WEIGHT: {w.Factor}");
            }
            else if (w.Weight > 1m)
            {
                issues.Add($"WEIGHT_ABOVE_ONE: {w.Factor}");
            }
        }

        var sum = weights.Where(w => w.IsActive).Sum(w => w.Weight);
        if (Math.Abs(sum - 1m) > WeightSumTolerance)
        {
            issues.Add($"WEIGHT_SUM: {sum.ToString("0.0000", CultureInfo.InvariantCulture)}");
        }

        return new RiskConfigValidation(issues.Count == 0, issues);
    }

    /// <summary>
    /// ตรวจ threshold: ระดับไม่ซ้ำ, <c>min &lt; max</c>, และเรียงแล้วครอบ 0–100 ต่อเนื่อง (ไม่มีช่องว่าง/ทับ).
    /// </summary>
    public static RiskConfigValidation ValidateThresholds(IReadOnlyCollection<RiskThresholdConfig>? thresholds)
    {
        var issues = new List<string>();
        if (thresholds is null || thresholds.Count == 0)
        {
            issues.Add("NO_THRESHOLDS");
            return new RiskConfigValidation(false, issues);
        }

        var levels = new HashSet<RiskLevel>();
        foreach (var t in thresholds)
        {
            if (!Enum.IsDefined(t.Level))
            {
                issues.Add($"UNKNOWN_LEVEL: {(int)t.Level}");
            }
            else if (!levels.Add(t.Level))
            {
                issues.Add($"DUPLICATE_LEVEL: {t.Level}");
            }

            if (t.MinScore >= t.MaxScore)
            {
                issues.Add($"EMPTY_RANGE: {t.Level}");
            }
        }

        var ordered = thresholds.OrderBy(t => t.MinScore).ToList();
        if (ordered[0].MinScore != MinScore)
        {
            issues.Add("RANGE_NOT_STARTING_AT_0");
        }

        if (ordered[^1].MaxScore != MaxScore)
        {
            issues.Add("RANGE_NOT_ENDING_AT_100");
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].MinScore != ordered[i - 1].MaxScore)
            {
                issues.Add($"RANGE_GAP_OR_OVERLAP: {ordered[i - 1].Level}/{ordered[i].Level}");
            }
        }

        return new RiskConfigValidation(issues.Count == 0, issues);
    }

    // ------------------------------------------------------------ classification

    /// <summary>
    /// จัดระดับ (FR-17 · BR-01) — <c>min &lt;= score &lt; max</c>, ช่วงบนสุดรวม 100.
    /// คืน null เมื่อ score นอก [0,100] หรือไม่ตกช่วงใด (config ผิด).
    /// </summary>
    public static RiskLevel? Classify(decimal score, IReadOnlyCollection<RiskThresholdConfig> thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        if (score < MinScore || score > MaxScore || thresholds.Count == 0)
        {
            return null;
        }

        var ordered = thresholds.OrderBy(t => t.MinScore).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i];
            var isTop = i == ordered.Count - 1;
            if (score >= t.MinScore && (score < t.MaxScore || (isTop && score <= t.MaxScore)))
            {
                return t.Level;
            }
        }

        return null;
    }

    // ------------------------------------------------------------ normalization

    /// <summary>
    /// normalize ปัจจัยทั้ง 9 เป็น [0,1] พร้อมแหล่งที่มา. ปัจจัยที่ไม่มีข้อมูล = ไม่อยู่ใน dictionary (ผู้เรียกตัดสิน EX-50).
    /// สมมติฐาน (ยังไม่มีแหล่งข้อมูลจริงใน MVP):
    /// BusinessImpact ← environment (Prod 1 / Staging 0.5 / Dev 0.25);
    /// ExploitAvailability ← KEV (ถูกใช้โจมตีจริง = 1, ไม่ทราบ = 0);
    /// ThreatIntel = 0 (ไม่มี feed);
    /// ExistingControl = 1 (ยังไม่มีบันทึก control → ถือว่าไม่มี — conservative).
    /// </summary>
    public static IReadOnlyDictionary<RiskFactor, (decimal Value, string Source)> Normalize(RiskInputs inputs, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var result = new Dictionary<RiskFactor, (decimal, string)>();

        if (inputs.CvssBaseScore is { } cvss && cvss >= 0m && cvss <= 10m)
        {
            result[RiskFactor.Cvss] = (cvss / 10m, "cvss");
        }
        else if (SeverityValue(inputs.Severity) is { } sev)
        {
            result[RiskFactor.Cvss] = (sev, "severity");
        }

        if (inputs.AssetCriticality is { } crit && CriticalityValue(crit) is { } c)
        {
            result[RiskFactor.AssetCriticality] = (c, "asset");
        }

        result[RiskFactor.BusinessImpact] = (inputs.Environment switch
        {
            AssetEnvironment.Prod => 1m,
            AssetEnvironment.Staging => 0.5m,
            _ => 0.25m,
        }, "environment");

        result[RiskFactor.InternetExposure] = (inputs.InternetExposure ? 1m : 0m, "asset");
        result[RiskFactor.ExploitAvailability] = inputs.IsKev ? (1m, "kev") : (0m, "unknown");
        result[RiskFactor.Kev] = (inputs.IsKev ? 1m : 0m, "reference");
        result[RiskFactor.ThreatIntel] = (0m, "unavailable");

        var ageDays = Math.Max(0d, (now - inputs.FirstDetectedAt).TotalDays);
        var age = Math.Min(1m, (decimal)ageDays / VulnAgeFullScaleDays);
        result[RiskFactor.VulnAge] = (Math.Round(age, 4, MidpointRounding.AwayFromZero), "firstDetectedAt");

        result[RiskFactor.ExistingControl] = (1m, "none-recorded");

        return result;
    }

    /// <summary>severity ที่ normalize แล้ว (Critical/High/Medium/Low/Informational) → [0,1]; ไม่รู้จัก = null.</summary>
    public static decimal? SeverityValue(string? severity) => severity?.Trim().ToUpperInvariant() switch
    {
        "CRITICAL" => 0.95m,
        "HIGH" => 0.75m,
        "MEDIUM" => 0.5m,
        "LOW" => 0.25m,
        "INFORMATIONAL" or "INFO" or "NONE" => 0m,
        _ => null,
    };

    public static decimal? CriticalityValue(AssetCriticality criticality) => criticality switch
    {
        AssetCriticality.Critical => 1m,
        AssetCriticality.High => 0.75m,
        AssetCriticality.Medium => 0.5m,
        AssetCriticality.Low => 0.25m,
        _ => null,
    };

    // ------------------------------------------------------------ calculation

    /// <summary>คำนวณ score + level + breakdown จาก factor ที่ normalize แล้ว (ใช้ตรงใน test ได้).</summary>
    public static RiskCalculation Calculate(
        IReadOnlyDictionary<RiskFactor, (decimal Value, string Source)> factors,
        IReadOnlyCollection<RiskWeightConfig> weights,
        IReadOnlyCollection<RiskThresholdConfig> thresholds)
    {
        ArgumentNullException.ThrowIfNull(factors);

        var weightCheck = ValidateWeights(weights);
        if (!weightCheck.IsValid)
        {
            return RiskCalculation.Fail(RiskErrorCodes.InvalidRiskWeight, weightCheck.Issues);
        }

        var thresholdCheck = ValidateThresholds(thresholds);
        if (!thresholdCheck.IsValid)
        {
            return RiskCalculation.Fail(RiskErrorCodes.InvalidRiskThreshold, thresholdCheck.Issues);
        }

        var active = weights.Where(w => w.IsActive && w.Weight > 0m).ToList();
        var missing = active.Where(w => !factors.ContainsKey(w.Factor)).Select(w => $"MISSING_FACTOR: {w.Factor}").ToList();
        if (missing.Count > 0)
        {
            return RiskCalculation.Fail(RiskErrorCodes.RiskInputIncomplete, missing);
        }

        var parts = new List<RiskFactorScore>(active.Count);
        var raw = 0m;
        foreach (var w in active.OrderBy(w => w.Factor))
        {
            var (value, source) = factors[w.Factor];
            var normalized = Math.Clamp(value, 0m, 1m);
            var contribution = w.Weight * normalized * MaxScore;
            raw += contribution;
            parts.Add(new RiskFactorScore(
                w.Factor.ToString(),
                Math.Round(normalized, 4, MidpointRounding.AwayFromZero),
                w.Weight,
                Math.Round(contribution, 4, MidpointRounding.AwayFromZero),
                source));
        }

        // EX-51: ผลนอกช่วงเกินความคลาดเคลื่อน = error (ไม่ clamp ปิดปัญหา)
        if (raw < MinScore - ScoreTolerance || raw > MaxScore + ScoreTolerance)
        {
            return RiskCalculation.Fail(
                RiskErrorCodes.RiskScoreOutOfRange,
                [$"RAW_SCORE: {raw.ToString("0.####", CultureInfo.InvariantCulture)}"]);
        }

        var score = ClampScore(raw);
        var level = Classify(score, thresholds);
        if (level is null)
        {
            return RiskCalculation.Fail(RiskErrorCodes.RiskScoreOutOfRange, ["NO_MATCHING_THRESHOLD"]);
        }

        var breakdown = new RiskBreakdown(
            ModelName, parts, Math.Round(raw, 4, MidpointRounding.AwayFromZero), score, level.Value.ToString());
        return new RiskCalculation(true, score, level.Value, breakdown, null, []);
    }

    /// <summary>normalize + calculate ในขั้นเดียว.</summary>
    public static RiskCalculation Calculate(
        RiskInputs inputs,
        IReadOnlyCollection<RiskWeightConfig> weights,
        IReadOnlyCollection<RiskThresholdConfig> thresholds,
        DateTime now)
        => Calculate(Normalize(inputs, now), weights, thresholds);

    /// <summary>invariant: ผลอยู่ใน [0,100] เสมอ และมีทศนิยมไม่เกิน 2 ตำแหน่ง.</summary>
    public static decimal ClampScore(decimal value)
        => Math.Round(Math.Clamp(value, MinScore, MaxScore), 2, MidpointRounding.AwayFromZero);

    // ------------------------------------------------------------ manual override

    /// <summary>ความยาวสูงสุดของเหตุผลการปรับ.</summary>
    public const int MaxReasonLength = 2000;

    /// <summary>
    /// ตรวจคำขอปรับ risk ด้วยตนเอง (FR-20): reason บังคับ (ไม่ว่าง/ไม่ใช่ช่องว่าง, ≤ <see cref="MaxReasonLength"/>, ไม่มี control char
    /// ยกเว้นขึ้นบรรทัด), rowVersion บังคับ (base64), score ∈ [0,100] ทศนิยม ≤ 2, riskLevel เป็นชื่อระดับที่รู้จัก,
    /// และต้องมี score หรือ riskLevel อย่างน้อยหนึ่งค่า.
    /// </summary>
    public static (ValidatedRiskOverride? Value, IReadOnlyDictionary<string, string[]> Errors) ValidateOverride(OverrideRiskRequest? request)
    {
        var errors = new Dictionary<string, string[]>();

        var reason = request?.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            errors["reason"] = [RiskErrorCodes.ReasonRequired];
        }
        else if (reason.Length > MaxReasonLength)
        {
            errors["reason"] = [RiskErrorCodes.ReasonTooLong];
        }
        else if (reason.Any(ch => char.IsControl(ch) && ch is not '\n' and not '\r'))
        {
            errors["reason"] = [RiskErrorCodes.InvalidFormat];
        }

        decimal? score = request?.Score;
        if (score is { } s && (s < MinScore || s > MaxScore || decimal.Round(s, 2) != s))
        {
            errors["score"] = [RiskErrorCodes.InvalidScore];
        }

        RiskLevel? level = null;
        var levelText = request?.RiskLevel?.Trim();
        if (!string.IsNullOrEmpty(levelText))
        {
            // ไม่รับตัวเลข (Enum.TryParse ยอมรับ "1") — ต้องเป็นชื่อระดับเท่านั้น
            if (!levelText.All(char.IsLetter)
                || !Enum.TryParse<RiskLevel>(levelText, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                errors["riskLevel"] = [RiskErrorCodes.InvalidRiskLevel];
            }
            else
            {
                level = parsed;
            }
        }

        if (score is null && string.IsNullOrEmpty(levelText))
        {
            errors["score"] = [RiskErrorCodes.OverrideValueRequired];
        }

        var rowVersion = ParseRowVersion(request?.RowVersion, required: true, out var rvError);
        if (rvError is not null)
        {
            errors["rowVersion"] = [rvError];
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new ValidatedRiskOverride(score, level, reason!, rowVersion!), errors);
    }

    /// <summary>แปลง rowVersion (base64, 8 ไบต์ของ SQL Server rowversion). ไม่ส่งมาและไม่บังคับ = null โดยไม่มี error.</summary>
    public static byte[]? ParseRowVersion(string? value, bool required, out string? errorCode)
    {
        errorCode = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errorCode = RiskErrorCodes.RowVersionRequired;
            }

            return null;
        }

        var buffer = new byte[16];
        if (!Convert.TryFromBase64String(value.Trim(), buffer, out var written) || written != 8)
        {
            errorCode = RiskErrorCodes.InvalidRowVersion;
            return null;
        }

        return buffer[..written];
    }

    // ------------------------------------------------------------ serialization

    public static string SerializeBreakdown(RiskBreakdown breakdown) => JsonSerializer.Serialize(breakdown, Json);

    public static string SerializeBreakdown(RiskManualBreakdown breakdown) => JsonSerializer.Serialize(breakdown, Json);
}
