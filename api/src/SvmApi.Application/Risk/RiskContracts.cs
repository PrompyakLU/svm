using SvmApi.Domain.Assets;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Vulnerabilities;

namespace SvmApi.Application.Risk;

/// <summary>น้ำหนักหนึ่งแถวจาก <c>config_riskWeight</c> (อ่าน runtime — NFR-31).</summary>
public sealed record RiskWeightConfig(RiskFactor Factor, decimal Weight, bool IsActive = true);

/// <summary>ช่วงคะแนนหนึ่งแถวจาก <c>config_riskThreshold</c> (BR-01).</summary>
public sealed record RiskThresholdConfig(RiskLevel Level, decimal MinScore, decimal MaxScore);

/// <summary>
/// ข้อมูลดิบของปัจจัยความเสี่ยงของ instance หนึ่งรายการ (ก่อน normalize).
/// <para><paramref name="CvssBaseScore"/> 0–10 (ถ้าไม่มี/ผิดช่วง ใช้ <paramref name="Severity"/> ที่ normalize แล้วแทน).</para>
/// <para><paramref name="AssetCriticality"/> null = ยังไม่กำหนด → คำนวณไม่ได้ (EX-50).</para>
/// </summary>
public sealed record RiskInputs(
    decimal? CvssBaseScore,
    string? Severity,
    AssetCriticality? AssetCriticality,
    AssetEnvironment Environment,
    bool InternetExposure,
    bool IsKev,
    DateTime FirstDetectedAt);

/// <summary>คะแนนของปัจจัยหนึ่งตัวใน breakdown (explainability — KD#1).</summary>
public sealed record RiskFactorScore(
    string Factor,
    decimal Normalized,
    decimal Weight,
    decimal Contribution,
    string Source);

/// <summary>breakdown ที่เก็บเป็น JSON ใน <c>trans_riskScoreHistory.factorBreakdown</c>.</summary>
public sealed record RiskBreakdown(
    string Model,
    IReadOnlyList<RiskFactorScore> Factors,
    decimal RawScore,
    decimal Score,
    string RiskLevel);

/// <summary>breakdown ของการปรับด้วยตนเอง (FR-20) — ค่าเดิม/ค่าใหม่.</summary>
public sealed record RiskManualBreakdown(
    string Model,
    decimal? PreviousScore,
    string? PreviousRiskLevel,
    decimal Score,
    string RiskLevel,
    bool LevelDerivedFromScore);

/// <summary>ผลการคำนวณ: สำเร็จ (score+level+breakdown) หรือ error code (EX-50/51/54) — ห้ามนำ score ที่ผิดไปใช้.</summary>
public sealed record RiskCalculation(
    bool Succeeded,
    decimal Score,
    RiskLevel Level,
    RiskBreakdown? Breakdown,
    string? ErrorCode,
    IReadOnlyList<string> Issues)
{
    public static RiskCalculation Fail(string code, IReadOnlyList<string> issues)
        => new(false, 0m, default, null, code, issues);
}

/// <summary>ผลตรวจ configuration (weight/threshold).</summary>
public sealed record RiskConfigValidation(bool IsValid, IReadOnlyList<string> Issues);

/// <summary>คำขอ recalc (rowVersion optional — ถ้าส่งมาต้องตรงกับค่าปัจจุบัน).</summary>
public sealed record RecalculateRiskRequest(string? RowVersion);

/// <summary>
/// คำขอปรับ risk ด้วยตนเอง (US-017 · FR-20): ต้องมี <c>reason</c> + <c>rowVersion</c> และอย่างน้อยหนึ่งใน score/riskLevel.
/// ส่ง score อย่างเดียว = จัดระดับจาก threshold; ส่ง riskLevel = override ระดับตรง ๆ.
/// </summary>
public sealed record OverrideRiskRequest(decimal? Score, string? RiskLevel, string? Reason, string? RowVersion);

/// <summary>คำขอปรับที่ผ่าน validation แล้ว.</summary>
public sealed record ValidatedRiskOverride(decimal? Score, RiskLevel? Level, string Reason, byte[] RowVersion);

/// <summary>ผล risk ของ instance หลัง recalc/override.</summary>
public sealed record InstanceRiskDto(
    Guid InstanceId,
    decimal Score,
    string RiskLevel,
    string CalculationType,
    decimal? PreviousScore,
    string? PreviousRiskLevel,
    Guid HistoryId,
    string RowVersion,
    RiskBreakdown? Breakdown);

public static class RiskErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>EX-54: weight ติดลบ/เกิน 1/ซ้ำ/ผลรวม active ≠ 1.</summary>
    public const string InvalidRiskWeight = "INVALID_RISK_WEIGHT";

    /// <summary>threshold ไม่ครอบ 0–100 ต่อเนื่อง หรือซ้ำ/ทับกัน.</summary>
    public const string InvalidRiskThreshold = "INVALID_RISK_THRESHOLD";

    /// <summary>EX-50: ข้อมูลไม่พอคำนวณ (เช่น ไม่มี CVSS/severity หรือยังไม่กำหนด asset criticality).</summary>
    public const string RiskInputIncomplete = "RISK_INPUT_INCOMPLETE";

    /// <summary>EX-51: ผลออกนอกช่วง 0–100.</summary>
    public const string RiskScoreOutOfRange = "RISK_SCORE_OUT_OF_RANGE";

    public const string ReasonRequired = "RISK_REASON_REQUIRED";
    public const string ReasonTooLong = "TOO_LONG";
    public const string InvalidFormat = "INVALID_FORMAT";
    public const string InvalidScore = "INVALID_SCORE";
    public const string InvalidRiskLevel = "INVALID_RISK_LEVEL";
    public const string OverrideValueRequired = "RISK_OVERRIDE_VALUE_REQUIRED";
    public const string RowVersionRequired = "ROW_VERSION_REQUIRED";
    public const string InvalidRowVersion = "INVALID_ROW_VERSION";

    public const string InstanceNotFound = "INSTANCE_NOT_FOUND";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}

public enum RiskOutcome
{
    Success,
    NotFound,

    /// <summary>มีอยู่แต่อยู่นอก data scope (EX-07): ผู้เรียกบันทึก denied-access และตอบเหมือน NotFound.</summary>
    OutOfScope,

    ValidationFailed,

    /// <summary>คำนวณไม่ได้เพราะข้อมูล/configuration (EX-50/51/54) → 422.</summary>
    Unprocessable,

    Conflict,
}

public sealed record RiskResult<T>(
    RiskOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Code = null,
    string? Message = null)
{
    public static RiskResult<T> Ok(T value) => new(RiskOutcome.Success, value);

    public static RiskResult<T> Fail(RiskOutcome outcome, string code, string? message = null)
        => new(outcome, default, null, code, message);

    public static RiskResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors, string code = RiskErrorCodes.ValidationError)
        => new(RiskOutcome.ValidationFailed, default, errors, code);
}

/// <summary>
/// RiskScoringEngine (C6 · US-015/016/017 · FR-16/17/20/21). ทุกการเขียน (instance + history + audit) อยู่ใน unit of work เดียว;
/// instance ถูกกรองด้วย data scope (นอก scope = 404).
/// </summary>
public interface IRiskScoringService
{
    /// <summary>คำนวณใหม่จาก weight/threshold ปัจจุบัน (FR-21) — บันทึก history ประเภท Recalc.</summary>
    Task<RiskResult<InstanceRiskDto>> RecalculateAsync(Guid instanceId, RecalculateRiskRequest? request, CancellationToken cancellationToken);

    /// <summary>ปรับ score/level ด้วยตนเองพร้อมเหตุผล (FR-20) — บันทึกค่าเดิม/ค่าใหม่/เหตุผล/ผู้แก้ไข/เวลา.</summary>
    Task<RiskResult<InstanceRiskDto>> OverrideAsync(Guid instanceId, OverrideRiskRequest request, CancellationToken cancellationToken);
}
